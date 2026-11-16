# Povijest projekta i stanje rada

Ovaj dokument služi za nastavak rada na drugom računalu, s Claude Codeom ili bez njega.
Ažurirano: 2026-11-15.

## Cilj

CALIX-C d.o.o. je računovodstveni servis. Alat iz PDF-a **"Brutto bilanca po kontima"** (izvoz iz knjigovodstvenog programa) izrađuje FINA obrazac **GFI-POD** (.xls).
Popunjava listove Bilanca, RDG, Dodatni podaci i Referentnu stranicu.
Prvi klijent i uzorak podataka je **AMBRA WATER SOLUTIONS d.o.o.**

Pravila FINA-e: https://www.fina.hr/javne-usluge-za-poslovne-subjekte/registri/registar-godisnjih-financijskih-izvjestaja/obrasci-financijskih-izvjestaja

## Tijek rada

1. **Faza 1 – konzolna aplikacija** (`src/Bilanca2Gfi.Cli`). Završena, a korisnik je potvrdio da logika radi.
2. **Faza 2 – web aplikacija "Calix-c Helper"**:
   - ASP.NET Core API (`src/CalixHelper.Api`) i React + Vite + Mantine sučelje (`web/`).
   - Header sadrži logo i naziv, lijevo je navigacija, desno forma.
   - Prva funkcija u navigaciji je Bilanca2Gfi. Nove funkcije se dodaju u `web/src/features/registry.tsx`.
   - Ulaz je upload datoteka, izlaz je download. Ne koriste se putanje do mapa na serveru.
3. **Privremena prijava**:
   - Modalni prozor, korisnik `sanja-calix` i lozinka `agrosanja`, zapisani u `appsettings.json` → `Auth:Users`.
   - Koristi se cookie autentifikacija, a sav `/api/bilanca2gfi` zahtijeva prijavu.
   - Kasnije slijedi prava autentifikacija.
4. **Git**: repozitorij je https://github.com/nikadrob/bilanca2gfi (privatan, jer sadrži podatke klijenta).

## Odluke korisnika (ne mijenjati bez dogovora)

- **Izlaz** je originalni FINA `.xls` predložak (`templates/GFI-POD-6.0.2.xls`). Nije `.xlsx`, jer FINA prima samo `.xls`.
- **Rezultat godine** se računa iz razreda 4 i 7. Porez na dobit unosi korisnik i on ide na AOP 185 i 121.
- **Prethodna godina**: RDG i Dodatni podaci prethodne godine uzimaju se iz prošlogodišnjeg GFI-a. Bilanca prethodne godine računa se iz početnog stanja (PS) u PDF-u.
- **Ulaz** je samo PDF.
- **Krediti se ne dijele na kratkoročni dio**: 252 → AOP 116 (kratkoročni), 945 → AOP 104 (dugoročni).
- **Pojedina konta**:
  - 12009 i 22400 ostaju bruto.
  - 22220 → obveze prema zaposlenicima (AOP 120).
  - 037 se oduzima.
- **Mapiranje** prati kontni plan AMBRE, a to je stari RRiF plan. `kontni_plan.pdf` je okvirni plan iz NN 75/2016 i razlikuje se od njega. Primjeri iz AMBRINOG plana: 23x plaće, 24x porezi, 25x dugoročni krediti, 28x rezerviranja, 46x ostali troškovi, 95x dobit.
- **Jezik i alati**: C# (.NET 10), jer će kasnije biti backend servis. Za sučelje React 19, Vite 8 i Mantine 9.
- **Referentna stranica AMBRE**:
  - svrha predaje 3 (oboje);
  - status autonomnosti 1 (samostalno);
  - kapital 100 % domaći.
- **Hosting**: Azure App Service F1 (besplatni plan). Netlify i Vercel ne mogu pokretati .NET.

## Arhitektura

```
src/Bilanca2Gfi.Core     PDF parser (PdfPig), mapiranje, izračun, upis u .xls (NPOI 2.7.6), izvještaj
  Services/GfiGenerator  zajednička orkestracija za CLI i API
src/CalixHelper.Api      minimal API, Features/Auth, Features/Bilanca2Gfi, poslužuje wwwroot
src/Bilanca2Gfi.Cli      konzolna verzija
web/                     React sučelje; build ide u src/CalixHelper.Api/wwwroot
config/ambra.json        pravila konto → AOP, Dodatni podaci, fiksni podaci referentne stranice
templates/               FINA GFI-POD 6.0.2
tests/                   xUnit: parser, izračun, upis, API (38 testova)
```

Detalji izračuna, API-ja i pokretanja su u `README.md`.

### Tehničke napomene

- **NPOI** mora ostati na 2.7.x, jer je od verzije 2.8 pod OSMF EULA s naknadom. Upozorenja za ImageSharp potisnuta su u `Directory.Build.props`.
- **Parser** iznose raspoređuje u 8 stupaca prema desnom rubu riječi (x-pozicija). Sidra stupaca uzima iz D/P slova zaglavlja, pomaknutih za 34 pt.
- **Formule predloška** su skrivene, a ćelije s formulama zaključane. Program puni samo otključane ulazne ćelije i uspoređuje svoje zbrojeve s formulama predloška.
- **Raspored ćelija u predlošku**:
  - AOP je u stupcu G (Bilanca, RDG) ili H (Dodatni);
  - I je prethodna godina, J tekuća;
  - L je oznaka "+" ili "=".
- **Datumi na referentnoj stranici** su tekst u obliku `dd.MM.yyyy`.
- **Kontrole** FINA kontrola 97 i `#VALUE!` na listu `Kont` javljaju se zbog regionalnih postavki računala koje nisu hrvatske. To nije greška programa.
- **Mantine 9**: `Grid` ima `gap`, a ne `gutter`.
- **Testni paketi**: `Microsoft.AspNetCore.Mvc.Testing` je fiksiran na 10.0.15, jer verzija 11 ne radi s net10.0.

## Rezultat na uzorku AMBRA (2026)

- Aktiva = pasiva = 2.861.195,40 (prethodna godina 3.100.414,81).
- Dobit prije poreza: 52.344,99. Uz porez od 1.000 dobit razdoblja je 51.344,99, a to provjerava i test.
- Svih 287 konta je mapirano.
- `gfi_ambra_water_sol.xls` **nije** zlatni uzorak: odnosi se na razdoblje 01.01.–30.09.2026, bez troškova i s neuravnoteženim zbrojevima.

## Otvoreno / sljedeći koraci

1. **Deploy na Azure App Service F1 (Linux)**: workflow `.github/workflows/deploy.yml` je napravljen (2026-11-16). Pokreće se pri pushu na `main`, a radi build weba, `dotnet test`, publish i deploy. `Urls` je maknut iz `appsettings.json`, jer Azure sam određuje port, a lokalni port 5080 dolazi iz `launchSettings.json`. **Preostaje** ručno postavljanje Azurea i GitHuba prema `README.md` → "Deploy na Azure". Korisnik je odabrao Azure (Linux) umjesto Vercela, jer Vercel ne pokreće .NET, a Hobby plan je samo za nekomercijalnu upotrebu. Free račun nakon 30 dana treba nadograditi na Pay-As-You-Go. F1 ostaje besplatan, a preporučeno je postaviti budžet s upozorenjem.
2. **Prava autentifikacija** koja zamjenjuje privremenog korisnika. `AuthContext` i `LoginModal` u sučelju mogu ostati.
3. **Podaci o reviziji za AMBRU** (FINA kontrola 42):
   - AMBRA ima obvezu revizije, jer je aktiva prethodne godine 3,1 mil. > 2,5 mil., a prihodi 6,65 mil. > 5 mil.
   - Treba upisati `revidiran`, `oibRevizora` i `misljenjeRevizora` u `config/ambra.json` → `referentnaStranica`.
4. Treba unijeti **stvarni porez na dobit i broj zaposlenih**.
5. Treba zamijeniti **privremeni logo** `web/public/logo.svg`.
6. Dolaze **nove funkcije** u navigaciji, a korisnik će ih definirati.

## Postavljanje na novom računalu

```powershell
# preduvjeti: Git, .NET 10 SDK (winget install Microsoft.DotNet.SDK.10), Node.js 22+
git clone https://github.com/nikadrob/bilanca2gfi.git
cd bilanca2gfi
dotnet test                                  # mora proći 38 testova
cd web; npm install; npm run build; cd ..    # izgradi sučelje u wwwroot
dotnet run --project src\CalixHelper.Api     # http://localhost:5080
```

Za razvoj sučelja treba **dva terminala**: u prvom `dotnet run --project src\CalixHelper.Api` (API na :5080), u drugom `cd web; npm run dev` (http://localhost:5173). Kad API nije pokrenut, Vite javlja `ECONNREFUSED`.

Pokretači na desktopu (`*.bat`) nisu u repozitoriju. Na novom računalu napravi novi pokretač koji izvrši: `dotnet run --project <repo>\src\CalixHelper.Api`, a zatim otvori http://localhost:5080.
