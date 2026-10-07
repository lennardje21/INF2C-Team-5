# SQLite-migratie en implementatie in C#

## Wat is opgeleverd?

`data/cargohub.sqlite` bevat alle 80.471 records uit de 16 bestaande JSON-bestanden.
Alle gewone velden zijn afzonderlijke SQL-kolommen; er worden geen JSON-blobs opgeslagen.
De geneste `endpoint_access` van gebruikers is genormaliseerd naar `user_endpoint`
en `user_permission`. Daardoor zijn er 18 tabellen.

`data/schema.sql` is het volledige, uitvoerbare schema met primaire sleutels,
foreign keys, indexen en schemaversie 1 (`PRAGMA user_version`).
`scripts/migrate_to_sqlite.py` maakt de database reproduceerbaar aan en controleert
alle velden tegen de brondata, inclusief de gereconstrueerde gebruikersrechten.

De Python-API, providers en endpoints zijn niet aangepast en gebruiken dus nog
JSON. De oorspronkelijke JSON-bestanden zijn behouden als migratiebron en voor
controle. Wijzigingen aan die bestanden worden **niet** automatisch naar SQLite
gesynchroniseerd. Voor de nieuwe C#-applicatie wordt SQLite de actieve opslag.

## Migratie uitvoeren

Python 3 met de standaardbibliotheek is voldoende. Vanuit de repositoryroot:

```sh
python3 scripts/migrate_to_sqlite.py
```

De database is al aangemaakt. Het script weigert een bestaand doelbestand te
overschrijven. Voor een nieuwe import of controle gebruik je een ander pad:

```sh
python3 scripts/migrate_to_sqlite.py --source data --database /tmp/cargohub-check.sqlite
python3 -m unittest discover -s tests -v
```

Het script leest alle 16 bronbestanden, past het schema toe op een tijdelijk bestand
en importeert alle records in één transactie. Bij ontbrekende bestanden, onbekende
velden, ongeldige rechten, dubbele sleutels of ongeldige relaties faalt de migratie.
Na een veldvergelijking, `PRAGMA foreign_key_check` en `PRAGMA integrity_check`
wordt alleen de geverifieerde database gepubliceerd. Bij fouten blijft geen
gedeeltelijke doeldatabase achter. Bronbestanden worden nooit aangepast.

Stop bij de uiteindelijke overstap eerst writes in de oude applicatie. Importeer
de laatste JSON-versie naar een nieuw databasepad en laat C# vervolgens dat bestand
gebruiken. Vanaf dat moment schrijf je uitsluitend naar SQLite. De nu opgeleverde
database is een momentopname van de huidige repositorydata.

## Tabellen en relaties

| Tabel | Records | Primaire sleutel | Verwijzingen |
| --- | ---: | --- | --- |
| warehouse | 10 | id | — |
| client | 300 | id | — |
| supplier | 21 | id | — |
| item_line | 14 | id | — |
| item_group | 3 | id | — |
| item_type | 3 | id | — |
| location | 400 | id | warehouse_id → warehouse |
| item | 600 | id | item_line_id, item_group_id, item_type_id, supplier_id |
| inventory | 4.800 | (item_id, location_id) | item, location |
| order | 4.854 | id | client_id, ship_to_client_id, bill_to_client_id → client; warehouse_id |
| order_item | 26.498 | id | order_id → order; item_id → item |
| shipment | 6.132 | id | order_id → order |
| shipment_item | 33.660 | id | shipment_id → shipment; item_id → item |
| transfer | 800 | id | from_location_id, to_location_id → location |
| transfer_item | 2.370 | id | transfer_id → transfer; item_id → item |
| user | 6 | api_key | — |
| user_endpoint | 72 | (api_key, resource) | user |
| user_permission | 288 | (api_key, resource, method) | user_endpoint |

Kolomnamen volgen exact de JSON-velden. De volledige kolommen, typen en
`NOT NULL`-regels staan in `data/schema.sql`. `order` en `user` worden in SQL
steeds met dubbele aanhalingstekens geschreven, bijvoorbeeld `SELECT * FROM "order"`.

Foreign keys gebruiken `ON DELETE RESTRICT` voor bedrijfsdata: verwijderen van
een parent met gekoppelde records wordt geweigerd. Verwijder child-records expliciet
in dezelfde transactie als dat volgens jullie bedrijfsregels is toegestaan.
Gebruikersrechten worden wel automatisch verwijderd met hun gebruiker.
Foreign-keykolommen hebben indexen; de samengestelde inventory-sleutel indexeert
ook zoeken op `item_id`.

Een shipment verwijst naar één order. Order-, shipment- en transferitems staan
in afzonderlijke child-tabellen, niet als lijst in de parent. Inventory heeft
geen aparte `id`; gebruik altijd beide sleutelvelden.

## Typen in C#

| SQLite | C# | Opmerking |
| --- | --- | --- |
| INTEGER | long | IDs, aantallen en commodity_code; gebruik GetInt64 |
| REAL | double | unit_weight en unit_price; gebruik GetDouble |
| TEXT | string | Namen, codes, statussen en ongewijzigde datumstrings |
| NULL | nullable type | Bijvoorbeeld shipping_notes: string? |
| INTEGER 0/1 | bool | user_permission.allowed |

Postcodes, barcodes en telefoonnummers blijven tekst. Datums en timestamps zijn
zonder conversie overgenomen: interpreteer hun bestaande formaat expliciet wanneer
je ze naar `DateTime` of `DateTimeOffset` omzet. Maak voor nieuwe timestamps één
formaatkeuze, bijvoorbeeld UTC met de round-tripnotatie `"O"`.

Prijzen behouden het oorspronkelijke floating-pointtype. Als jullie exacte
geldberekeningen willen, maak dan een aparte schemamigratie naar integerbedragen
in de gekozen kleinste valuta-eenheid, met expliciete afrondingsregels.

Het schema is gebaseerd op de huidige brondata. Bijvoorbeeld `shipment.order_id`
is nu verplicht; een toekomstig C#-contract dat shipments zonder order ondersteunt
vereist een schemamigratie naar een nullable kolom. SQL-resultaten hebben zonder
`ORDER BY` geen gegarandeerde volgorde.

## Verbinden vanuit C#

Voeg in jullie nieuwe C#-project de provider toe:

```sh
dotnet add package Microsoft.Data.Sqlite
```

Onderstaand voorbeeld is een volledig consoleprogramma om de bestaande database
te openen en één inventory-record te lezen. Geef het absolute databasepad als
enige commandoregelargument. Het maakt geen nieuwe database aan als het pad fout is.

```csharp
using System;
using System.IO;
using Microsoft.Data.Sqlite;

if (args.Length != 1)
    throw new ArgumentException("Pass the path to cargohub.sqlite.");

var databasePath = Path.GetFullPath(args[0]);
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    Mode = SqliteOpenMode.ReadWrite,
    ForeignKeys = true,
    DefaultTimeout = 30
}.ToString();

using var connection = new SqliteConnection(connectionString);
connection.Open();

using (var version = connection.CreateCommand())
{
    version.CommandText = "PRAGMA user_version;";
    if (Convert.ToInt32(version.ExecuteScalar()) != 1)
        throw new InvalidOperationException("Unsupported database schema version.");
}

using var command = connection.CreateCommand();
command.CommandText = """
    SELECT item_id, location_id, quantity_on_hand, quantity_allocated
    FROM inventory
    WHERE item_id = $itemId AND location_id = $locationId;
    """;
command.Parameters.AddWithValue("$itemId", 1L);
command.Parameters.AddWithValue("$locationId", 1L);
using var reader = command.ExecuteReader();
if (reader.Read())
{
    Console.WriteLine($"Item {reader.GetInt64(0)}, location {reader.GetInt64(1)}: " +
        $"on hand {reader.GetInt64(2)}, allocated {reader.GetInt64(3)}");
}
else
{
    Console.WriteLine("Inventory record not found.");
}
```

De raw string in dit voorbeeld vereist C# 11 of hoger. Gebruik in de applicatie
een geconfigureerd absoluut pad naar een schrijfbare datamap en dezelfde
connection factory voor alle repositories. Zet `ForeignKeys = true` op iedere
connectie, omdat foreign-keyhandhaving per connectie wordt ingesteld.
Microsoft documenteert deze provider en instellingen in de
[providerhandleiding](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)
en de [connection-stringreferentie](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings).

## Writes en transacties

Repositories voeren SQL uit met parameters en expliciete kolomselecties. Services
bewaken bedrijfsregels en houden samenhangende wijzigingen in één transactie.
Gebruik geen lijst die bij iedere save een complete tabel overschrijft.

Dit fragment voert binnen een open connectie een voorraadcorrectie uit en controleert
dat precies één bestaande rij is gewijzigd:

```csharp
using var transaction = connection.BeginTransaction();
using var update = connection.CreateCommand();
update.Transaction = transaction;
update.CommandText = """
    UPDATE inventory
    SET quantity_on_hand = quantity_on_hand + $delta,
        updated_at = $updatedAt
    WHERE item_id = $itemId AND location_id = $locationId
      AND quantity_on_hand + $delta >= 0;
    """;
update.Parameters.AddWithValue("$delta", 5L);
update.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
update.Parameters.AddWithValue("$itemId", 1L);
update.Parameters.AddWithValue("$locationId", 1L);
if (update.ExecuteNonQuery() != 1)
    throw new InvalidOperationException("Inventory missing or correction invalid.");
transaction.Commit();
```

Voer iedere aanvullende wijziging voor dezelfde operatie vóór `Commit()` uit op
dezelfde connectie en met dezelfde `Transaction`. Bij een exception zorgt disposal
van de transactie voor rollback. Zie de officiële
[transactiedocumentatie](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions).

Bij de port van de bestaande bedrijfslogica moeten jullie onder andere deze
operaties atomair implementeren:

- Order aanmaken/wijzigen/verwijderen samen met de bijbehorende `order_item`-regels.
- Orderitems wijzigen samen met de benodigde `quantity_allocated`-correcties.
- Shipmentitems wijzigen samen met de benodigde `quantity_ordered`-correcties.
- Transfer uitvoeren samen met voorraadmutaties op bron- en doellocatie en de statuswijziging.

SQLite foreign keys bewaken relaties; voorraadberekeningen en statusovergangen
worden niet automatisch uitgevoerd door deze database. Definieer en test die
regels in de C#-servicelaag. Nieuwe IDs kunnen bij `INTEGER PRIMARY KEY` worden
gegenereerd door de `id`-kolom weg te laten bij INSERT; lees het resultaat met
`last_insert_rowid()` op dezelfde connectie. Bereken nieuwe IDs niet met `MAX(id)+1`.

## Gebruikers en rechten

Een gebruiker heeft een `api_key` en `app`. Per resource bewaart `user_endpoint`
de aanwezigheid van de resource; `user_permission` bewaart iedere opgegeven methode
en de expliciete booleanwaarde. Ook false-rechten zijn behouden. Zo gaan geen
gegevens uit de geneste bronstructuur verloren.

Gebruik deze geparametriseerde query om één recht te controleren:

```sql
SELECT allowed
FROM user_permission
WHERE api_key = $apiKey AND resource = $resource AND method = $method;
```

Methodewaarden in de bron zijn lowercase: `get`, `post`, `put`, `delete`.
Normaliseer een aangeleverde methode met `ToLowerInvariant()`. Een ontbrekende
rij of `allowed = 0` betekent geen toegang. Controleer het bestaan van de gebruiker
apart als je onderscheid wilt maken tussen onbekende gebruiker en ontbrekend recht.
De bestaande API-keys zijn ongewijzigd overgenomen; behandel het databasebestand
dus met dezelfde toegangsbeperkingen als de oorspronkelijke gebruikersdata.

## Praktische invoering

1. Voeg de provider, connection factory en repositorylaag aan het C#-project toe.
2. Map de bestaande SQL-namen expliciet. Bij EF Core: onder andere
   `ToTable("order")`, snake_case-kolomnamen, `HasKey(x => new { x.ItemId, x.LocationId })`
   voor inventory en `ApiKey` als gebruikerssleutel.
3. Gebruik de meegeleverde database als bestaande versie 1. Voer `schema.sql`
   alleen uit op een lege database; dat bestand importeert zelf geen data.
   Laat een ORM geen nieuwe database over dit bestand heen aanmaken. Richt vóór
   latere EF-migraties een bij dit schema passende baseline in.
4. Test repository-CRUD, geweigerde foreign keys, nullable velden, rechten en rollback
   van samengestelde bedrijfsoperaties in een tijdelijke databasekopie.
5. Voer bij de overstap de laatste import uit en configureer C# met dat databasepad.
6. Beheer latere schemawijzigingen als versiegebonden SQL- of ORM-migraties;
   verhoog `user_version` pas na een geslaagde migratie.

Gebruik een persistent datavolume bij deployment. Overschrijf een actieve database
niet bij een build of release. Maak backups bij stilgelegde writes of via de SQLite
backup-API; bij gebruik van WAL hoort een losse kopie van alleen het hoofdbestand
niet bij een betrouwbare live backup. JSON-bronbestanden kunnen worden verwijderd
nadat de C#-overstap en de onafhankelijke backup zijn afgerond.

## Verificatie van deze oplevering

Alle 80.471 oorspronkelijke records zijn veld voor veld gecontroleerd. De
72 resources en 288 methodepermissies reconstrueren exact de rechten van de
6 gebruikers. De database-integriteitscontrole en foreign-keycontrole zijn geslaagd.
De zeven automatische tests controleren volledige gegevensoverdracht,
overschrijfbeveiliging, primaire sleutels, foreign keys, foutopruiming, onbekende
velden en booleanrechten. Er is nog geen C#-applicatie of endpointimplementatie
toegevoegd; de C#-fragmenten hierboven dienen als implementatiehandleiding.
