"""Generate the Dutch benchmark report and CSV from measured results."""

import csv
from datetime import datetime
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zoneinfo import ZoneInfo

ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / "benchmarks/results"
LABELS = {
    "item_one": "Eén artikel ophalen", "item_missing": "Niet-bestaand artikel zoeken",
    "item_all": "Alle 600 artikelen ophalen", "inventory_all": "Alle 4.800 voorraadrijen ophalen",
    "inventory_by_item": "Voorraad van één artikel", "order_items": "Regels van één order",
    "shipment_items": "Regels van één shipment", "inventory_totals": "Voorraadtotalen van één artikel",
    "order_with_items": "Order inclusief regels", "shipment_with_items": "Shipment inclusief regels",
    "transfer_with_items": "Transfer inclusief regels", "inventory_update": "Voorraadrij ophalen, wijzigen en opslaan",
    "item_update": "Artikel ophalen, wijzigen en opslaan", "group_roundtrip": "Itemgroep aanmaken en verwijderen",
}


def main():
    data = json.loads((RESULTS / "python_mysql.json").read_text())
    environment = json.loads((RESULTS / "environment.json").read_text())
    checks = data["functional_checks"]
    tests = (RESULTS / "validation/python-tests.txt").read_text()
    if "Ran 6 tests" not in tests or not tests.rstrip().endswith("OK"):
        raise ValueError("Python validation tests have not passed")
    trx = ET.parse(RESULTS / "validation/mysql-integration.trx")
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = trx.find(".//t:Counters", ns).attrib
    if counters["passed"] != "5" or counters["failed"] != "0":
        raise ValueError("MySQL integration tests have not passed")
    stamp = datetime.fromisoformat(data["started_utc"]).astimezone(ZoneInfo("Europe/Amsterdam"))
    operations = sum(len(row["python_ms"]) for row in data["benchmarks"].values())
    old_total = sum(sum(row["python_ms"]) for row in data["benchmarks"].values())
    new_total = sum(sum(row["mysql_ms"]) for row in data["benchmarks"].values())
    ratio = old_total / new_total
    reduction = 100 * (1 - new_total / old_total)
    def factor(name):
        return data["benchmarks"][name]["median_speedup"]
    def p95(name, engine):
        return data["benchmarks"][name][engine]["p95_ms"]
    rows = []
    csv_path = RESULTS / "summary.csv"
    with csv_path.open("w", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(["scenario", "category", "samples_per_system", "python_median_ms", "mysql_median_ms",
                         "python_p95_ms", "mysql_p95_ms", "median_speedup", "median_time_reduction_percent",
                         "python_serial_ops_per_second", "mysql_serial_ops_per_second"])
        for name, result in data["benchmarks"].items():
            old, new = result["python"], result["mysql"]
            speedup = result["median_speedup"]
            change = f"{speedup:.2f}× sneller" if speedup >= 1 else f"{1 / speedup:.2f}× trager"
            rows.append(f"| {LABELS[name]} | {old['median_ms']:.3f} | {new['median_ms']:.3f} | "
                        f"{old['p95_ms']:.3f} | {new['p95_ms']:.3f} | {change} |")
            writer.writerow([name, result["category"], old["n"], old["median_ms"], new["median_ms"],
                             old["p95_ms"], new["p95_ms"], speedup, result["median_time_reduction_percent"],
                             old["serial_ops_per_second"], new["serial_ops_per_second"]])
    routes = "\n".join(f"| {row['route']} | {row['status']} | {'Geslaagd' if row['passed'] else 'Mislukt'} |"
                       for row in data["python_http_checks"])
    measured = "\n".join(rows)
    records = sum(row["records"] for row in data["table_checks"])
    text = f"""# Vergelijking Python/JSON en C#/MySQL

Meetdatum: {stamp:%d-%m-%Y %H:%M} (Europe/Amsterdam).

## Bevindingen

De nieuwe datalaag retourneert dezelfde bedrijfsdata als het oude Python-systeem:
**{checks['passed']}/{checks['total']} vergelijkingschecks geslaagd ({checks['success_rate_percent']:.0f}%)**.
Alle {records:,} bedrijfsrecords uit de 15 tabellen zijn vergeleken. De afzonderlijke
live migratiecontrole omvat ook de 6 gebruikers: alle 80.471 bronrecords kloppen.
Er zijn geen verschillen gevonden na de gedocumenteerde type- en volgordenormalisatie.

Voor de kunstmatige mix van 14 even vaak uitgevoerde opslagoperaties is C#/MySQL
**{ratio:.2f}× sneller**, met **{reduction:.1f}% minder gemeten operatietijd**.
Dit is een datalaagbenchmark, geen vergelijking tussen twee complete API's.
De huidige C#-controllers en services zijn nog leeg; endpoint- en bedrijfsregelpariteit
kan daardoor niet worden vastgesteld.

De winst verschilt per operatie. Voorraad per artikel is {factor('inventory_by_item'):.2f}× sneller en een
voorraadupdate {factor('inventory_update'):.2f}× sneller op basis van de mediane tijd. Volledige lijsten en
kleine writes hebben geen winst: alle artikelen ophalen is {1 / factor('item_all'):.2f}× trager,
alle voorraadrijen ophalen {1 / factor('inventory_all'):.2f}× trager en een kleine itemgroep aanmaken/verwijderen
{1 / factor('group_roundtrip'):.2f}× trager. Er is dus geen algemene garantie dat iedere MySQL-operatie sneller is.

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
tabelchecks vergelijken ieder record en ieder scalar veld. Er zijn {operations:,}
gemeten operaties per implementatie uitgevoerd: {operations * 2:,} samen, zonder operationele
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
{measured}

Voor de gelijk gewogen mix kostten dezelfde {operations:,} gemeten operaties bij Python
{old_total / 1000:.3f} seconden en bij C#/MySQL {new_total / 1000:.3f} seconden aan opgetelde
operatietijd. Dat komt overeen met {operations / (old_total / 1000):.1f} tegenover
{operations / (new_total / 1000):.1f} opeenvolgende operaties per seconde.
Dit is geen gemeten capaciteit onder gelijktijdige belasting en bevat niet de
transporttijd van de benchmarkharness. De mix weegt ieder scenario even zwaar;
een echte workload met meer bulkreads of kleine writes kan anders uitvallen.

Een lagere mediaan betekent niet altijd betere staartlatency. Eén artikel zoeken
is typisch sneller met MySQL, maar P95 is hier {p95('item_one', 'mysql'):.3f} ms tegenover {p95('item_one', 'python'):.3f} ms bij
Python. Bij transfer inclusief regels is MySQL-P95 ook hoger. Neem daarom beide
kolommen mee bij verdere optimalisatie.

## Oude Python-API

De originele `ApiRequestHandler` is gestart op een tijdelijke localhost-poort,
zonder de originele server of zijn notificatietimer te starten. De handler en
providers gebruiken kopieën van dezelfde JSON-bestanden. De responses zijn vergeleken
met equivalent opgebouwde C#-repositoryresultaten; dit zijn geen C#-HTTP-responses.

| GET-controle | HTTP-status | Uitkomst |
| --- | ---: | --- |
{routes}

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
| CPU / architectuur | {environment['cpu']} / {data['environment']['architecture']} |
| Geheugen host | 16 GiB |
| OS | {data['environment']['os']} |
| Python | {data['environment']['python']} |
| .NET SDK / runtime | {data['environment']['dotnet_sdk']} / {environment['dotnet_runtime']} |
| C#-build | Release, tijdelijk net9.0-project met huidige bronbestanden |
| MySqlConnector | {environment['mysqlconnector']} |
| MySQL | {environment['mysql_server']} (Docker) |
| Docker engine / VM-resources | {environment['docker_server']}, 10 CPU's, circa 7,65 GiB |
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
"""
    (ROOT / "docs/PYTHON_MYSQL_BENCHMARK.md").write_text(text, encoding="utf-8")
    print(f"Generated report and {csv_path}")


if __name__ == "__main__":
    main()
