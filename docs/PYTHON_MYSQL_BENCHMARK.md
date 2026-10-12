# Vergelijking Python/JSON en C#/MySQL

Meetdatum: 12-10-2026 13:34 (Europe/Amsterdam).

## Bevindingen

De nieuwe datalaag retourneert dezelfde bedrijfsdata als het oude Python-systeem:
**165/165 vergelijkingschecks geslaagd (100%)**.
Alle 80,465 bedrijfsrecords uit de 15 tabellen zijn vergeleken. De afzonderlijke
live migratiecontrole omvat ook de 6 gebruikers: alle 80.471 bronrecords kloppen.
Er zijn geen verschillen gevonden na de gedocumenteerde type- en volgordenormalisatie.

Voor de kunstmatige mix van 14 even vaak uitgevoerde opslagoperaties is C#/MySQL
**3.52× sneller**, met **71.6% minder gemeten operatietijd**.
Dit is een datalaagbenchmark, geen vergelijking tussen twee complete API's.
De huidige C#-controllers en services zijn nog leeg; endpoint- en bedrijfsregelpariteit
kan daardoor niet worden vastgesteld.

De winst verschilt per operatie. Voorraad per artikel is 6.62× sneller en een
voorraadupdate 12.92× sneller op basis van de mediane tijd. Volledige lijsten en
kleine writes hebben geen winst: alle artikelen ophalen is 1.49× trager,
alle voorraadrijen ophalen 1.12× trager en een kleine itemgroep aanmaken/verwijderen
16.83× trager. Er is dus geen algemene garantie dat iedere MySQL-operatie sneller is.

## Succespercentages en dekking

| Controle | Geslaagd / uitgevoerd | Succespercentage |
| --- | ---: | ---: |
| Volledige bedrijfsdatatabellen vergelijken | 15 / 15 | 100% |
| Oude Python-API: zes responses en ongeldige API-key | 7 / 7 | 100% |
| Resultaatvergelijking na een meetbatch | 140 / 140 | 100% |
| Na writes opnieuw lezen en vergelijken | 3 / 3 | 100% |
| Totaal vergelijkingschecks | 165 / 165 | 100% |
| Python-export-/mappingtests | 6 / 6 | 100% |
| C#-MySQL-integratietests | 5 / 5 | 100% |

Deze noemers tellen testchecks, geen gebruikers of productieaanvragen. De 15
tabelchecks vergelijken ieder record en ieder scalar veld. Er zijn 2,800
gemeten operaties per implementatie uitgevoerd: 5,600 samen, zonder operationele
exceptions. De 140 batchchecks vergelijken het laatste resultaat na iedere batch;
de 2.800 afzonderlijke outputs zijn niet ieder apart vergeleken. De 20 warm-upoperaties
per scenario en de initiële tabel-/HTTP-checks tellen niet mee in de prestatiestatistiek.

De vijf integratietests controleren alle modeltypes, CRUD, unchanged updates,
UTC, samengestelde sleutels, foreign keys, decimal-/propertymapping, NULL en rollback.
De Python-tests controleren volledige seedoverdracht, herhaalbaarheid, C#-kolommapping,
escaping/Unicode, UTC-conversie en het weigeren van ongeldige verwijzingen.

## Snelheidsmetingen

200 herhalingen per scenario en per implementatie. Tijden in milliseconden;
lager is beter. Mediaan is de typische gemeten latency; P95 is het 95e percentiel.
De factor is Python-mediaan gedeeld door C#/MySQL-mediaan.

| Operatie | Python mediaan | MySQL mediaan | Python P95 | MySQL P95 | MySQL tegenover Python |
| --- | ---: | ---: | ---: | ---: | --- |
| Eén artikel ophalen | 0.880 | 0.466 | 1.037 | 1.304 | 1.89× sneller |
| Niet-bestaand artikel zoeken | 0.890 | 0.428 | 1.025 | 1.035 | 2.08× sneller |
| Alle 600 artikelen ophalen | 0.878 | 1.311 | 1.043 | 2.907 | 1.49× trager |
| Alle 4.800 voorraadrijen ophalen | 3.365 | 3.758 | 3.692 | 6.231 | 1.12× trager |
| Voorraad van één artikel | 3.445 | 0.520 | 3.831 | 1.087 | 6.62× sneller |
| Regels van één order | 12.840 | 2.679 | 13.801 | 7.002 | 4.79× sneller |
| Regels van één shipment | 13.102 | 2.973 | 15.974 | 7.673 | 4.41× sneller |
| Voorraadtotalen van één artikel | 3.485 | 0.451 | 3.872 | 1.594 | 7.72× sneller |
| Order inclusief regels | 19.436 | 3.034 | 20.274 | 5.993 | 6.41× sneller |
| Shipment inclusief regels | 20.372 | 3.468 | 23.308 | 8.018 | 5.87× sneller |
| Transfer inclusief regels | 1.448 | 0.865 | 1.668 | 2.273 | 1.67× sneller |
| Voorraadrij ophalen, wijzigen en opslaan | 17.650 | 1.366 | 19.240 | 3.996 | 12.92× sneller |
| Artikel ophalen, wijzigen en opslaan | 4.674 | 1.349 | 4.930 | 2.511 | 3.47× sneller |
| Itemgroep aanmaken en verwijderen | 0.124 | 2.085 | 0.280 | 3.976 | 16.83× trager |

Voor de gelijk gewogen mix kostten dezelfde 2,800 gemeten operaties bij Python
20.861 seconden en bij C#/MySQL 5.932 seconden aan opgetelde
operatietijd. Dat komt overeen met 134.2 tegenover
472.0 opeenvolgende operaties per seconde.
Dit is geen gemeten capaciteit onder gelijktijdige belasting en bevat niet de
transporttijd van de benchmarkharness. De mix weegt ieder scenario even zwaar;
een echte workload met meer bulkreads of kleine writes kan anders uitvallen.

Een lagere mediaan betekent niet altijd betere staartlatency. Eén artikel zoeken
is typisch sneller met MySQL, maar P95 is hier 1.304 ms tegenover 1.037 ms bij
Python. Bij transfer inclusief regels is MySQL-P95 ook hoger. Neem daarom beide
kolommen mee bij verdere optimalisatie.

## Oude Python-API

De originele `ApiRequestHandler` is gestart op een tijdelijke localhost-poort,
zonder de originele server of zijn notificatietimer te starten. De handler en
providers gebruiken kopieën van dezelfde JSON-bestanden. De responses zijn vergeleken
met equivalent opgebouwde C#-repositoryresultaten; dit zijn geen C#-HTTP-responses.

| GET-controle | HTTP-status | Uitkomst |
| --- | ---: | --- |
| /api/v1/items/600 | 200 | Geslaagd |
| /api/v1/items | 200 | Geslaagd |
| /api/v1/inventories | 200 | Geslaagd |
| /api/v1/orders/4854 | 200 | Geslaagd |
| /api/v1/shipments/6132 | 200 | Geslaagd |
| /api/v1/transfers/800 | 200 | Geslaagd |
| invalid API key | 401 | Geslaagd |

De C#-benchmark vult voor order, shipment en transfer de childregels expliciet aan,
om dezelfde responsevorm als Python te vergelijken. Deze samenstelling gebeurt
alleen in de benchmarkharness: de productie-repository vult parentcollections
niet automatisch en de services moeten dit nog implementeren.

## Testopzet

1. Start een aparte MySQL 8.4-container met schema en seed; gebruik geen projectdatabase
   voor writes. Python krijgt een tijdelijke volledige kopie van de JSON-data.
2. Vergelijk de 15 tabellen voordat er writes worden uitgevoerd en test de Python-API.
3. Voer per scenario 20 warm-upoperaties uit om JIT, connectionpool en caches op te warmen.
4. Meet 10 rondes met batches van 20 operaties. Scenario- en enginevolgorde worden
   met vaste random seed 20261012 gewisseld; reads en writes hebben aparte groepen.
5. Vergelijk outputs na iedere batch en lees gewijzigde tabellen opnieuw na afloop.
6. Controleer hashes van de originele JSON-bestanden: deze zijn ongewijzigd gebleven.

Python roept de echte modelmethodes via `data_provider` aan, inclusief opnieuw
openen/parsen van JSON per operatie. Dat is ook wat de bestaande HTTP-handler doet.
C# roept de bestaande `MySqlRepository<T>` aan, inclusief pooled connectie,
SQL-uitvoering en modelmaterialisatie. De timing sluit HTTP, authenticatie,
benchmark-IPC en uiteindelijke JSON-response-serialisatie uit. Het samenstellen
van parent/childresultaten hoort wel bij de betreffende relatieoperatie.

Updates omvatten lezen, wijzigen en opslaan. Python herschrijft zijn hele JSON-file;
MySQL voert een rij-update uit en commit een transactie. De itemgroep-roundtrip
doet aan beide kanten twee persistente writes: insert en delete. Python wordt
voor alle schrijftests alleen wat betreft timestamps gepatcht naar één vaste UTC-tijd
om resultvergelijking reproduceerbaar te maken; de echte laad-/wijzig-/savecode blijft
ongewijzigd. Arrays worden gesorteerd voor de vergelijking; API-volgorde wordt niet getest.
Timestamps worden naar UTC genormaliseerd, propertynamen naar snake_case en floats
worden voor vergelijking via hun decimale tekstweergave genormaliseerd.

## Omgeving

| Onderdeel | Waarde |
| --- | --- |
| CPU / architectuur | Apple M4 / arm64 |
| Geheugen host | 16 GiB |
| OS | macOS-26.5.2-arm64-arm-64bit |
| Python | 3.12.10 |
| .NET SDK / runtime | 9.0.304 / 9.0.8 |
| C#-build | Release, tijdelijk net9.0-project met huidige bronbestanden |
| MySqlConnector | 2.6.2 |
| MySQL | 8.4.11 (Docker) |
| Docker engine / VM-resources | 28.3.3, 10 CPU's, circa 7,65 GiB |
| Parallelle operaties | 1 |
| InnoDB commitinstelling | innodb_flush_log_at_trx_commit = 1 |

Het echte C#-project blijft `net10.0`. Hier is alleen de .NET 9 SDK beschikbaar;
de benchmark en vijf integratietests gebruiken daarom tijdelijke .NET 9-projecten
met exact dezelfde model-, repository- en testbestanden. De volledige .NET 10-build
en prestaties na endpointintegratie moeten nog opnieuw worden gevalideerd.

## Verklaring en beperkingen

Gerichte MySQL-queries beperken het aantal te lezen records. Python moet bijvoorbeeld
voor orderregels alle 26.498 regels inlezen en doorlopen; de repository filtert
op `order_id`. Een voorraadupdate herschrijft bij Python 4.800 rijen, terwijl
MySQL één rij wijzigt. Dit verklaart de grootste winst in deze workload.

Bij een volledige lijst moet MySQL nog steeds alles uitlezen, over het lokale
Docker/TCP-pad transporteren en met reflection naar models omzetten. Python leest
een lokaal, warm gecachet JSON-bestand. Bij drie kleine itemgroepen kan de overhead
van twee MySQL-commits veel groter zijn dan twee kleine JSON-writes.

De durability is bovendien verschillend: de Python-save gebruikt geen expliciete
`fsync` of transactie; MySQL gebruikt InnoDB-commit met flushinstelling 1. De
logical write-resultaten zijn gelijk, maar garanties bij stroomuitval of crashes
zijn niet gelijk en zijn niet getest. MySQL gebruikt een lokale Docker-VM terwijl
Python native draait. Het rapport is geen geïsoleerde vergelijking van programmeertalen.

Er is één lokale meetsessie uitgevoerd, zonder gelijktijdige gebruikers, koude
cache, netwerklatency op afstand, failover, geheugenprofielen, lange duurbelasting
of grotere datasets. De empirische 100% is geen beschikbaarheidsgarantie. Order-
allocaties, voorraadreserveringen, transfercommit en autorisatie in nieuwe endpoints
zijn nog geen C#-bedrijfsimplementatie en vallen buiten deze pariteitsclaim.

## Aanbevolen vervolg

- Verbind controllers en services met de repositories en vul childcollections bewust.
- Voeg pagination en gerichte projections toe voor grotere volledige lijsten.
- Test bedrijfsoperaties atomair, met concurrency en rollback, voordat de oude API wordt vervangen.
- Herhaal de benchmark met .NET 10 en vergelijk dan de HTTP-API's inclusief
  authenticatie en JSON-responses, met een representatieve verdeling van aanvragen.

## Bewijs en herhalen

- [Alle ruwe timingmetingen en checks](../benchmarks/results/python_mysql.json).
- [CSV met medianen, P95 en sequentiële throughput](../benchmarks/results/summary.csv).
- [Omgeving en broncodehashes](../benchmarks/results/environment.json).
- [Live MySQL-veldcontrole](../benchmarks/results/validation/mysql-data.txt).
- [Python-testuitvoer](../benchmarks/results/validation/python-tests.txt).
- [C#-testresultaten (TRX)](../benchmarks/results/validation/mysql-integration.trx).
- [Reproduceerinstructies en benchmarkcode](../benchmarks/README.md).

De gemeten projectdatabase is read-only gecontroleerd. Alle benchmarkwrites draaiden
in de aparte testdatabase en tijdelijke JSON-kopieën. De testcontainer wordt na
deze oplevering verwijderd; de normale MySQL-projectdatabase blijft draaien.
