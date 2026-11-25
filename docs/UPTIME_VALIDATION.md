# Validatie uptimeberekening: UP → DOWN → UP

Uitgevoerd op 25 november 2026. De berekening is gecontroleerd met reproduceerbare
heartbeatgegevens en de ongewijzigde berekeningscode van Uptime Kuma **2.5.12**,
de versie van de beschikbare instance. De productiehistorie bevatte bij controle
alleen UP-heartbeats. Daarom zijn gecontroleerde testgegevens gebruikt; er is
geen productie-uitval veroorzaakt. Dit bewijst de berekening, niet de werkelijk
behaalde beschikbaarheid van de productieserver.

## Methode

Dezelfde fixtures gaan naar `UptimeService.Calculate()` en naar Kuma's eigen
`UptimeCalculator.update()` gevolgd door `getData(168, "hour")`.
De referentie draait in de officiële Docker-image met `TEST_BACKEND=1`, zodat
de berekening zonder database kan worden uitgevoerd. Er is geen eigen kopie
van Kuma's formule als referentie gebruikt.

- Image: `louislam/uptime-kuma:2.5.12`
- Image-digest: `sha256:8e4b6bef96417ec98a35dc103c99cc64fc229a01e584e56d95d9004d2003c02a`
- SHA-256 van de uitgevoerde `uptime-calculator.js`: `0790e97e5b68ee426917f114a210c82ab96aa212098dfed59da4e3720744464f`
- [Officiële broncode van de calculator](https://github.com/louislam/uptime-kuma/blob/2.5.12/server/uptime-calculator.js)

De volledige fixtures worden gegenereerd door
[`TransitionScenarios.cs`](../benchmarks/CargoHub.UptimeVerification/TransitionScenarios.cs).
De week loopt van 18 november 2026 11:59:59 UTC tot 25 november 2026 11:59:59 UTC.
De testgrenzen zijn afgestemd op Kuma's 168 uurbuckets, zodat verschillende
venstergrenzen de vergelijking niet verstoren.

## Heartbeatbewijs en handmatige berekening

Voor het scenario met 60 seconden downtime bevat de geschiedenis 10.080 checks,
waarvan 10.079 UP en één DOWN. Rond de overgang staan deze gegevens:

| Tijd UTC, 21 november 2026 | Status | Duration |
| --- | --- | ---: |
| 23:18:59 | UP (1) | 60 s |
| 23:19:59 | DOWN (0) | 60 s |
| 23:20:59 | UP (1) | 60 s |

CargoHub kent de verstreken tijd vóór een heartbeat toe aan de status van die
heartbeat. Het DOWN-interval telt daarom één keer als 60 seconden downtime.
De eerste UP-heartbeat na herstel telt weer als UP en voegt geen extra downtime toe.
Dit is een meetconventie; checks bepalen niet het exacte moment waarop een echte
storing tussen twee metingen begon of eindigde.

Een week bevat 604.800 seconden. Handmatig:

```text
DOWN = 60 seconden
UP = 604.800 − 60 = 604.740 seconden
CargoHub = 604.740 / 604.800 × 100 = 99,990079365…%
Kuma = 10.079 / 10.080 × 100 = 99,990079365…%
Toegestane downtime bij SLA 99,99% = 60,48 seconden
SLA gehaald = true
```

## Vergelijkingsresultaten

| Scenario | UP / DOWN in seconden | CargoHub | Kuma 2.5.12 | SLA CargoHub |
| --- | --- | ---: | ---: | --- |
| Vaste checks van 60 s, één DOWN | 604.740 / 60 | 99,990079% | 99,990079% | Gehaald |
| Vaste checks van 60 s, twee DOWN | 604.680 / 120 | 99,980159% | 99,980159% | Niet gehaald |
| Drie DOWN-retries van 20 s | 604.740 / 60 | 99,990079% | 99,970244% | Gehaald |
| Slechts 180 s bekende historie | 60 / 120 | 0,009921% | 50,000000% | Onbekend (`null`) |

Bij vaste intervallen en volledige dekking komen beide berekeningen overeen
binnen 0,000001 procentpunt. De herstelovergang telt geen interval dubbel.
Bij 120 seconden downtime wordt de SLA terecht niet gehaald.

Kuma 2.5.12 telt checks: bij drie korte DOWN-retries zijn er 10.079 UP-checks en
drie DOWN-checks. Dat levert `10.079 / 10.082 × 100 = 99,970244%` op.
CargoHub weegt de drie intervallen van 20 seconden samen als 60 seconden.
Het verschil is dus verklaarbaar door de gekozen meetmethode. De twee systemen
leveren niet onder alle omstandigheden hetzelfde percentage of SLA-oordeel op.

Bij onvolledige historie deelt CargoHub bevestigde UP-tijd door de volledige
week en rapporteert een ondergrens, 604.620 onbekende seconden en `slaMet: null`.
Kuma deelt in dit scenario twee UP-checks door vier bekende checks: 50%.
Dat is geen bewijs van 50% beschikbaarheid over een volledige week.
Daarnaast gebruikt Kuma uurbuckets en CargoHub een exact rollend weekvenster;
bij andere venstergrenzen kan ook daardoor een verschil ontstaan.

## Reproduceren en bewijsbestanden

Voer vanuit de repository uit met .NET 10 en een draaiende Docker-engine:

```sh
bash benchmarks/uptime/verify.sh
dotnet test 'C# WMS/tests/CargoHub.Tests/CargoHub.Tests.csproj' --configuration Release --filter 'FullyQualifiedName~Uptime|FullyQualifiedName~MetricsController'
```

De referentievergelijking slaagde voor alle vier scenario's. De lokale testselectie
leverde **37 geslaagde tests, nul fouten** op, inclusief vijf nieuwe overgangstests.
De vergelijking controleert zowel verwachte overeenkomsten als de hierboven
verklaarde verschillen; `passed: true` betekent dus niet bij ieder scenario
dat de percentages gelijk zijn.

- [JSON-resultaten](../benchmarks/results/uptime/transition-comparison.json): percentages, seconden, SLA, checks, invoerhash en calculatorhash.
- [Heartbeatfragmenten als CSV](../benchmarks/results/uptime/transition-heartbeats.csv): ruwe tijden, statussen en durations rondom iedere overgang.
- [Referentie-uitvoering](../benchmarks/uptime/kuma-reference.cjs): aanroepen van Kuma's echte calculator.
- [Unit tests](../C%23%20WMS/tests/CargoHub.Tests/Unit/UptimeTransitionTests.cs): controles op seconden, percentage, herstel en SLA.

Voor een testrapport kunnen de heartbeat-tabel, handmatige berekening en
resultatentabel hierboven worden overgenomen. Vermeld daarbij dat het om
gecontroleerde fixtures gaat. Gebruik het JSON-bestand en de CSV als bijlagen;
een screenshot van de productie-interface zou deze gesimuleerde overgang niet bewijzen.
