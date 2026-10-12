# MySQL-data en C#-implementatie

## Oplevering

Het MySQL 8.4-schema en de seed staan in `C# WMS/src/CargoHub.Api/Data/MySql`:

- `01-schema.sql`: 15 bedrijfsdatatabellen, 3 gebruikerstabellen en `schema_version`.
- `02-seed.sql`: alle 80.471 oorspronkelijke records, plus 72 genormaliseerde
  resourcevermeldingen en 288 methodepermissies. Geen JSON-blobs.
- `manifest.json`: aantallen en SHA-256-hashes van bronbestanden en seed.
- `verify.sql`: vergelijk de geïmporteerde aantallen met de verwachte aantallen.

De bestaande JSON-bestanden blijven in `Python WMS/data`. Ze worden niet automatisch
gesynchroniseerd met MySQL. Het exportscript `scripts/export_mysql.py` gebruikt deze
bestanden als bron.

De C#-models en endpoints zijn behouden. `MySqlConnectionFactory` en
`MySqlRepository<T>` zijn toegevoegd en geregistreerd voor dependency injection.
Repositories ondersteunen lezen, invoegen, wijzigen en verwijderen van alle 15
bestaande bedrijfsmodels. Controllers en services gebruiken deze repositories nog
niet: de endpointimplementatie blijft buiten deze datamigratie.

## Starten met een nieuwe database

Installeer Docker met een draaiende engine en de .NET 10 SDK. Vanuit `C# WMS`:

```sh
test -f .env || cp .env.example .env
```

Vul je eigen wachtwoorden in `.env` in en start:

```sh
docker compose up -d
docker compose logs -f mysql
```

Compose mount de twee SQL-bestanden read-only in `/docker-entrypoint-initdb.d`.
De MySQL-container voert ze in alfabetische volgorde uit bij initialisatie van
een **leeg** datavolume. Dit gedrag staat beschreven in de
[MySQL Docker-documentatie](https://dev.mysql.com/doc/refman/8.4/en/docker-mysql-more-topics.html).
Wacht totdat de initialisatie klaar is voordat je de applicatie laat verbinden.

## Bestaand datavolume

Bij een bestaand volume worden initialisatiescripts niet opnieuw uitgevoerd.
Verwijder geen volume met bestaande data om een import af te dwingen.
Maak eerst een backup en kies een lege doeldatabase. Schema en seed zijn bewust
niet idempotent: dubbele tabellen of sleutels geven een fout in plaats van
bestaande gegevens te overschrijven.

Als de geconfigureerde doeldatabase leeg is, voer vanuit `C# WMS` uit:

```sh
docker compose exec -T mysql sh -c 'MYSQL_PWD="$MYSQL_PASSWORD" exec mysql --default-character-set=utf8mb4 -u"$MYSQL_USER" "$MYSQL_DATABASE"' < src/CargoHub.Api/Data/MySql/01-schema.sql
docker compose exec -T mysql sh -c 'MYSQL_PWD="$MYSQL_PASSWORD" exec mysql --default-character-set=utf8mb4 -u"$MYSQL_USER" "$MYSQL_DATABASE"' < src/CargoHub.Api/Data/MySql/02-seed.sql
docker compose exec -T mysql sh -c 'MYSQL_PWD="$MYSQL_PASSWORD" exec mysql -u"$MYSQL_USER" "$MYSQL_DATABASE"' < src/CargoHub.Api/Data/MySql/verify.sql
```

Gebruik geen `--force`: de client moet bij een fout stoppen. Het schema bevat DDL
en is in MySQL niet transactioneel. Een mislukte schema-import kan dus tabellen
achterlaten; herstel eerst de lege doeldatabase voordat je opnieuw importeert.
De volledige seed gebruikt één InnoDB-transactie. Bij een fout moet de client
stoppen en de verbinding sluiten, zodat ongecommitteerde inserts terugrollen.
Foreign-keychecks worden nooit uitgezet.

## Export reproduceren

Vanuit de repositoryroot, met Python 3 en uitsluitend de standaardbibliotheek:

```sh
python3 scripts/export_mysql.py
python3 -m unittest discover -s tests -v
python3 scripts/verify_mysql.py
```

Het script vernieuwt de seed en het manifest, niet het schema of een actieve
database. Het controleert velden, dubbele sleutels, verwijzingen, int-bereik,
decimalprecisie, booleanrechten en tijdzones vóór het schrijven. Pas bij een
nieuw veld eerst het SQL-schema en de modelmapping aan.

`verify_mysql.py` vergelijkt alle velden in de draaiende Compose-database met de
JSON-bronnen, inclusief gereconstrueerde gebruikersrechten. Het voert alleen SELECTs
uit en print geen API-keys of wachtwoorden. Verwacht dat deze controle verschillen
meldt zodra jullie applicatie de geïmporteerde data heeft gewijzigd.

Tekst wordt als UTF-8 hexliteral uitgevoerd; apostrofs, backslashes, regeleinden
en Unicode blijven behouden, onafhankelijk van SQL-escapinginstellingen.
Datums worden naar UTC `DATETIME(6)` omgezet. Decimalwaarden worden rechtstreeks
uit JSON als `Decimal` ingelezen en niet via een floating-pointconversie.
De seed bevat bestaande API-keys en hoort dezelfde toegangsbeperkingen te hebben
als de oorspronkelijke gebruikersdata.

## Schema en modelmapping

Tabelnamen volgen de JSON-bron: `warehouse`, `client`, `supplier`, `item_line`,
`item_group`, `item_type`, `location`, `item`, `inventory`, `order`, `order_item`,
`shipment`, `shipment_item`, `transfer`, `transfer_item`, `user`.
Gebruik backticks voor SQL-identifiers, bijvoorbeeld `SELECT * FROM \`order\``.

| C# | MySQL | Mapping |
| --- | --- | --- |
| int | INT | Alle IDs en aantallen |
| decimal UnitPrice | DECIMAL(18,4) | Exacte prijzen |
| decimal UnitWeight | DECIMAL(18,6) | Exacte gewichten |
| DateTime | DATETIME(6) | UTC; MySQL bewaart geen tijdzone |
| string | VARCHAR(255) of TEXT | utf8mb4 |
| string? ShippingNotes | TEXT NULL | NULL blijft behouden |
| Inventory | Samengestelde primary key | (item_id, location_id) |
| Item.MinPurchaseQuantity | min_purchase_qty | Expliciete uitzondering op snake_case |
| Order.Items e.d. | Child-tabellen | Geen kolom of automatisch opgeslagen collectie |

Andere properties worden naar snake_case gemapt. Tabellen met een `id` hebben
`AUTO_INCREMENT`; import behoudt oorspronkelijke IDs. De volgende inserts kunnen
IDs laten genereren. Alle bedrijfsverwijzingen gebruiken foreign keys met
`ON DELETE RESTRICT`. InnoDB maakt de benodigde foreign-keyindexen aan.
Een shipment heeft één `order_id`; inventory heeft geen afzonderlijke `id`.

Het schema sluit aan op de huidige models: een shipment zonder order is niet
toegestaan en statuswaarden blijven strings. Voor modelwijzigingen of extra
nullable velden is een expliciete schemamigratie nodig.

## C# verbinden en lezen

Het project bevat `MySqlConnector` 2.6.2. De provider documenteert
[verbindingen](https://mysqlconnector.net/tutorials/connect-to-mysql/) en
[installatie](https://mysqlconnector.net/overview/installing/).
Vanuit `C# WMS`, met dezelfde credentials als `.env`:

```sh
export ConnectionStrings__CargoHub='Server=localhost;Port=3307;Database=cargohub;User ID=cargohub;Password=YOUR_PASSWORD'
dotnet run --project src/CargoHub.Api
```

Docker Compose leest `.env`, maar `dotnet run` doet dat niet automatisch.
Gebruik de environmentvariabele hierboven, user secrets of jullie deployment
secretvoorziening. Het appsettings-veld is bewust leeg. Voor een C#-applicatie
binnen hetzelfde Compose-netwerk gebruik je host `mysql` en poort `3306`.

Voor deze lokale oplevering is `.env` aangemaakt met gegenereerde wachtwoorden en
is `cargohub-mysql` gestart op poort 3307, met data in het persistente
Compose-volume. `.env` is genegeerd door Git en is niet onderdeel van de oplevering
aan andere teamleden. Gebruik lokaal het wachtwoord uit dat bestand; overschrijf
een bestaande `.env` niet met de voorbeeldwaarden.

Injecteer bijvoorbeeld `MySqlRepository<Item>` in een service:

```csharp
using CargoHub.Api.Data;
using CargoHub.Api.Models;

public sealed class ItemLookup(MySqlRepository<Item> items)
{
    public async Task<Item?> FindAsync(int id, CancellationToken cancellationToken)
    {
        var rows = await items.GetAsync(
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return rows.SingleOrDefault();
    }
}
```

Filters gebruiken SQL-kolomnamen, worden tegen de modelmapping gecontroleerd
en gebruiken parameters. Lezen zonder filters retourneert alle rijen, gesorteerd
op de primary key. Voeg voor grote endpointresultaten later pagination toe.
Lees orderitems bijvoorbeeld via `MySqlRepository<OrderItem>` met filter
`order_id`; vul daarna expliciet `Order.Items`. Hetzelfde geldt voor shipments
en transfers. Parentcollections blijven leeg totdat jullie service ze vult.

## Schrijven en bedrijfsregels

Writes vereisen een bestaande open connectie en transactie. Zo kunnen meerdere
repositories onderdeel zijn van één bedrijfsoperatie. Voorbeeld van een insert,
binnen een service met geïnjecteerde factory en repository:

```csharp
await using var connection = await factory.OpenAsync(cancellationToken);
await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
var now = DateTime.UtcNow;
var group = new ItemGroup
{
    Name = "New group", Description = "Example", CreatedAt = now, UpdatedAt = now
};
await groups.InsertAsync(group, connection, transaction, cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

Bij `Id = 0` laat `InsertAsync` MySQL een ID genereren en vult het model met dat ID.
Timestampvelden stel je in de service in. De factory gebruikt UTC en matched-row
semantiek, zodat een update met dezelfde waarden niet ten onrechte ontbrekend lijkt.
`UpdateAsync` en `DeleteAsync` vereisen precies één gematchte rij. De
inventory-sleutel bevat altijd zowel `ItemId` als `LocationId`.

Geef voor alle reads en writes binnen een samengestelde operatie dezelfde connectie
en transactie door, ook aan de `GetAsync`-overload. Bij een exception zonder commit
zorgt disposal van de transactie voor rollback. Een gegenereerd ID in een object
wordt na rollback niet automatisch gewist; hergebruik dat object niet als nieuwe insert.

Implementeer voorraadallocatie, order-/shipmentregelwijzigingen en transfercommit
in de servicelaag, steeds met parent, childrecords en voorraad in dezelfde transactie.
De repository slaat alleen scalar properties op. Foreign keys vervangen geen
bedrijfsregels en wijzigen geen voorraad of statussen automatisch. Gebruik voor
gelijktijdige voorraadwijzigingen een atomaire geparametriseerde UPDATE of
`SELECT ... FOR UPDATE` op dezelfde transactie.

## Gebruikersrechten

`user` bewaart `api_key` en `app`. `user_endpoint` bewaart de beschikbare resources;
`user_permission` bewaart methode en expliciet booleanrecht, inclusief false.
API-keys en resources gebruiken een case-sensitive collation.
De bestaande bedrijfsmodels bevatten geen User-model; authenticatie wordt apart
geïmplementeerd en is niet aangesloten op de endpoints.

```sql
SELECT allowed FROM user_permission
WHERE api_key = @apiKey AND resource = @resource AND method = @method;
```

Normaliseer HTTP-methodes naar lowercase (`get`, `post`, `put`, `delete`). Geen rij
of `allowed = 0` betekent geen toegang. Controleer apart of de gebruiker bestaat
als je onbekende gebruiker en ontbrekend recht wilt onderscheiden.

## Overstap en validatie

Stop writes in Python voordat je de laatste export maakt. Importeer die export
in een lege MySQL-database, controleer `verify.sql`, implementeer de C#-services
en schakel daarna over. Vanaf dat moment is MySQL de actieve opslag en worden
JSON-bestanden niet meer bijgewerkt. Bewaar backups en beheer latere schemawijzigingen
als genummerde migraties; wijzig een al gebruikte initialisatiescriptversie niet
als vervanging voor een productie-upgrade.

De exporttests vergelijken alle 80.471 records veld voor veld met de seed,
reconstrueren gebruikersrechten, controleren C#-kolommapping, deterministische
export, Unicode/escaping, UTC-conversie en het weigeren van ongeldige relaties.
Dat is geen vervanging voor een live import- en repositorytest op MySQL.

Er zijn ook vijf optionele C#-integratietests. Geef een **afzonderlijke**, volgens
de schema- en seedbestanden gevulde testdatabase mee:

```sh
export CARGOHUB_TEST_MYSQL='Server=localhost;Port=13307;Database=cargohub_check;User ID=root;Password=YOUR_TEST_PASSWORD'
dotnet test 'C# WMS/tests/CargoHub.Tests/CargoHub.Tests.csproj'
```

Zonder deze variabele worden alleen de MySQL-integratietests overgeslagen. De tests
controleren de import via alle models, CRUD, UTC, unchanged updates, samengestelde
sleutels, foreign keys, decimal-/propertymapping, NULL en transactierollback.
Gebruik ze niet op een productie- of gedeelde ontwikkelingsdatabase.

Bij deze oplevering zijn alle velden van de 80.471 records in de lokale MySQL 8.4
database gecontroleerd tegen de bron. Zes Python-tests en vijf C#-integratietests
zijn geslaagd. De integratietests draaiden tegen een aparte tijdelijke container.
Omdat hier alleen de .NET 9 SDK is geïnstalleerd, zijn de ongewijzigde model-,
repository- en testbestanden in tijdelijke .NET 9-projecten gecompileerd en getest.
Het daadwerkelijke project blijft `net10.0`; de volledige solutionbuild vereist
nog validatie met de .NET 10 SDK.
