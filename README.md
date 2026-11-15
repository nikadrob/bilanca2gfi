# Calix-c Helper

Web aplikacija (C# / ASP.NET Core + React / Vite / Mantine) s alatima za računovodstvo.
Prva funkcija je **Bilanca2Gfi**: iz PDF-a **bruto bilance po kontima** izrađuje FINA obrazac **GFI-POD**.
Popunjava Bilancu, RDG, Dodatne podatke i referentnu stranicu u originalnom FINA `.xls` predlošku.

## Struktura

```
src/Bilanca2Gfi.Core   poslovna logika: PDF parser, mapiranje konto → AOP, izračun, upis u .xls, izvještaj
src/CalixHelper.Api    ASP.NET Core API + posluživanje web sučelja (wwwroot)
src/Bilanca2Gfi.Cli    konzolna verzija (ista logika)
web/                   React + Vite + Mantine sučelje
config/                pravila mapiranja po tvrtki (JSON)
templates/             FINA predložak GFI-POD 6.0.2
tests/                 xUnit testovi (parser, izračun, upis, API)
```

## Pokretanje

**Uobičajeno.** Dvaput klikni `Calix-c Helper.bat` na desktopu, ili pokreni:

```
dotnet run --project src/CalixHelper.Api
```

Zatim otvori http://localhost:5080. Ako web sučelje još nije izgrađeno, prvo pokreni `npm install` i `npm run build` u mapi `web/`. Build sprema datoteke u `src/CalixHelper.Api/wwwroot`.

**Razvoj sučelja** (promjene se odmah vide u pregledniku). Pokreni dva procesa:

```
dotnet run --project src/CalixHelper.Api     # API na :5080
cd web && npm run dev                        # Vite na :5173, /api se prosljeđuje na :5080
```

Zatim otvori http://localhost:5173. Kad promijeniš kod sučelja, za produkcijsku verziju ponovi `npm run build`.

**Testovi:** `dotnet test`

## Bilanca2Gfi u web sučelju

1. Učitaj **PDF bruto bilance**. Po želji učitaj i **prošlogodišnji GFI (.xls)**: iz njega se uzimaju RDG i Dodatni podaci prethodne godine te podaci referentne stranice.
2. Upiši porez na dobit i broj zaposlenih, pa klikni **Kreiraj**.
3. Prikazuje se rezime: aktiva i pasiva, prihodi i rashodi, dobit, greške i upozorenja kontrola. Ispod su gumbi za preuzimanje `.xls` obrasca i izvještaja o kontrolama.

Datoteke svakog pokretanja spremaju se u `src/CalixHelper.Api/App_Data/jobs/<id>/` i brišu se nakon 7 dana (`appsettings.json` → `Bilanca2Gfi`).

API:
- `POST /api/bilanca2gfi` (multipart: `pdf`, `prethodniGfi?`, `porez?`, `zaposleni?`, `zaposleniSati?`)
- `GET /api/bilanca2gfi/{id}/xls`
- `GET /api/bilanca2gfi/{id}/izvjestaj`

## Konzolna verzija

```
dotnet run --project src/Bilanca2Gfi.Cli -- --mapa <mapa> --porez 9.876,54 --zaposleni 21 --zaposleni-sati 20
```

Sve opcije ispisuje `--pomoc`. Izlazni kod je `0` kad je sve u redu, `2` kad su u kontrolama greške, a `1` kad je rad prekinut.

## Kako radi izračun

1. **Parser** čita PDF s koordinatama riječi i svaki iznos smješta u jedan od 8 stupaca prema x-poziciji. Provjerava `PS + promet = ukupno` i `saldo = ukupno D − P` za svako konto, međuzbrojeve grupa i redak SVEUKUPNO.
2. **Mapiranje** (`config/<tvrtka>.json`) dodjeljuje konto AOP-u po najduljem prefiksu. Aktiva i rashodi računaju se kao D − P, a pasiva i prihodi kao P − D. Konto bez pravila je greška.
3. **Izračun**:
   - Tekuća godina računa se iz salda, a Bilanca prethodne godine iz početnog stanja.
   - Rezultat godine računa se iz razreda 4 i 7.
   - Porez ulazi u AOP 185 i AOP 121.
   - Zbirne pozicije računaju se po formulama predloška.
4. **Upis** puni samo ulazne ćelije predloška. Formule predloška se izračunavaju i uspoređuju s izračunom programa.
5. **Referentna stranica** se popunjava tri puta, a svaki korak ima prednost pred prethodnim:
   - podaci iz prošlogodišnjeg GFI-a;
   - `referentnaStranica` iz konfiguracije;
   - podaci izvedeni iz razdoblja i ostali automatski podaci.

   Program usto provjerava obvezu revizije, istim pravilom kao FINA kontrola 42.

## Nova tvrtka

Kopiraj `config/ambra.json`, upiši dio naziva tvrtke u `prepoznavanje` i prilagodi pravila njezinom kontnom planu. Izvještaj navodi svako konto koje nema pravilo.

## Napomene

- FINA prima isključivo `.xls`, pa izlaz nije `.xlsx`.
- Kontrole na listu `Kont` koje rade s datumima ispravno se računaju samo uz hrvatske regionalne postavke (FINA kontrola br. 97).
- NPOI je namjerno na verziji 2.7.x (Apache-2.0). Od verzije 2.8 binarni paket nosi OSMF EULA s naknadom za komercijalne korisnike.
- **Privremena prijava.** Korisnik i lozinka zapisani su u `appsettings.json` (`Auth:Users`). Prijava koristi kolačić `calix.auth` (HttpOnly, SameSite=Strict, traje 8 h). Svi `/api/bilanca2gfi` pozivi traže prijavu. Kasnije će se zamijeniti pravom autentifikacijom.
  - Endpointi: `POST /api/auth/login` (`{username, password}`), `POST /api/auth/logout`, `GET /api/auth/me`.
  - Na Azureu lozinku nadjačaj postavkama aplikacije (`Auth__Users__0__Password`) da ne bude samo ona iz repozitorija.
