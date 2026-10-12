# Reproduceerbare Python/JSON versus C#/MySQL-vergelijking

De benchmark verandert geen applicatiecode. De echte Python-modelmethodes worden
op tijdelijke JSON-kopieën aangeroepen; de echte C#-repositories op een aparte
MySQL-testdatabase. Gebruik een lege, geïsoleerde database voor iedere volledige run.

Benodigd: Docker, Python 3 en .NET 10. Voer uit vanuit de repositoryroot.

```sh
docker run -d --name cargohub-benchmark -p 127.0.0.1:13307:3306 \
  -e MYSQL_ROOT_PASSWORD=benchmark-only \
  -e MYSQL_DATABASE=cargohub_benchmark \
  --mount "type=bind,source=$PWD/C# WMS/src/CargoHub.Api/Data/MySql/01-schema.sql,target=/docker-entrypoint-initdb.d/01-schema.sql,readonly" \
  --mount "type=bind,source=$PWD/C# WMS/src/CargoHub.Api/Data/MySql/02-seed.sql,target=/docker-entrypoint-initdb.d/02-seed.sql,readonly" \
  mysql:8.4
docker logs -f cargohub-benchmark
```

Wacht op de voltooide import en de definitieve `ready for connections` na
`MySQL init process done`, stop de logweergave en voer uit:

```sh
dotnet build benchmarks/CargoHub.Benchmarks/CargoHub.Benchmarks.csproj -c Release
export CARGOHUB_BENCH_MYSQL='Server=127.0.0.1;Port=13307;Database=cargohub_benchmark;User ID=root;Password=benchmark-only'
python3 benchmarks/compare_python_mysql.py \
  --dll benchmarks/CargoHub.Benchmarks/bin/Release/net10.0/CargoHub.Benchmarks.dll
```

Standaard: 14 scenario's, 20 warm-ups, 10 batches van 20 metingen per scenario.
Resultaten staan in `benchmarks/results/python_mysql.json`. Resultaatsortering
gebeurt buiten de getimede operatie; de uiteindelijke uitvoer wordt na ieder paar
batches vergeleken. Beide implementaties beginnen met dezelfde brondata en
voeren evenveel updates uit. Bestaande C#-endpointcode wordt niet vervangen.

Schrijfoperaties kunnen de testdatabase veranderen. Start voor een herhaling
opnieuw met een lege testcontainer; verander het testdatabasenaam/poortargument
niet naar de projectdatabase. De originele JSON-hashes worden gecontroleerd.

Voer de extra validaties uit en genereer het rapport:

```sh
mkdir -p benchmarks/results/validation
python3 -m unittest discover -s tests -v > benchmarks/results/validation/python-tests.txt 2>&1
export CARGOHUB_TEST_MYSQL="$CARGOHUB_BENCH_MYSQL"
dotnet test 'C# WMS/tests/CargoHub.Tests/CargoHub.Tests.csproj' \
  --logger 'trx;LogFileName=mysql-integration.trx' \
  --results-directory "$PWD/benchmarks/results/validation"
python3 scripts/verify_mysql.py > benchmarks/results/validation/mysql-data.txt
python3 benchmarks/render_report.py
```

`verify_mysql.py` controleert de normale projectdatabase alleen read-only en
verwacht dat die nog overeenkomt met de brondata. `environment.json` bevat de
omgeving en broncodehashes van de oorspronkelijke meetsessie; werk dat bestand
bij als je het rapport voor een nieuwe machine of broncodeversie genereert.

Verwijder na afloop uitsluitend de zelf aangemaakte testcontainer en zijn
anonieme testvolume:

```sh
docker rm -f -v cargohub-benchmark
```

De meegeleverde meting draaide met een tijdelijk .NET 9 Release-project, omdat
de .NET 10 SDK lokaal ontbreekt. Dat tijdelijke project linkte de huidige
`Models/*.cs`, `Data/*.cs` en `Program.cs` van de benchmark, gebruikte
`MySqlConnector` 2.6.2 en `Microsoft.AspNetCore.App` als frameworkreference en
een global using voor `Microsoft.Extensions.Configuration`. De productieprojecten
en het ingecheckte benchmarkproject blijven `net10.0`.
