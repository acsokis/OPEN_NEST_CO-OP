# syncfix.11 - ellenorzesi allapot, 2026-10-26

Kiserleti MelonLoader fork. Az upstream merfoldkovek nem jelentenek teljes
ketgepes szinkronigazolast.

| Terulet | Bizonyitek / korlat |
|---|---|
| Forditas | MelonLoader net6 build: 0 hiba; meglevo figyelmeztetesek |
| Automatizalt ellenorzesek | 136 sikeres; dokumentum-protokoll, hibas/sorrenden kivuli csomagok, ujracsatlakozas, valodi helyi TCP, adatkodekek, ajtoszandek es agyuazonositas |
| Ket teljes jatekpeldany | Egy PC-n host-vendeg handshake es kozos palyabetoltes naploban igazolt |
| Hatterterkep | Tester visszajelzes: exact MissionID javitas utan egyezik; korabban scene alapjan valasztottunk kartyat |
| Kezdeti terkepallapot | 26 native token kuldese/alkalmazasa egy probaban igazolt; nem teljes vizualis egyenloseg |
| Tablet | Mindket replica ready, host heartbeat fogadva; jegyzet/celkijeloles mindket iranyban tovabbi native tesztet igenyel |
| Ajto/lampa | Vendeg kivant-allapot protokoll es regi heartbeat vedelme implementalva; teljes native regresszio nincs igazolva |
| Terkeprajz | Native UpdateLine vegpontok es RestorePlacement implementalva; minden forma/cimke/megjegyzes nincs igazolva |
| Beszerzopult | 138-as kartya-lista regisztralva; stabil slotkulcs es pontos kartyakeres. Teljes vendeg-vasarlas nincs vegigigazolva |
| Jobb/bal agyu | Stabil hierarchia szerinti routing; minden toltesi kombinacio nincs igazolva |
| Scout plane | Lifecycle/animacio/FMOD implementalva; teljes native repules/foto/hang proba hianyzik |
| Terheles | Eros lag jelentve; tesztpeldanyok 960x540/20 FPS korlatot kaptak. Pontos lagok es megszunes meressel nincs igazolva |
| Ket kulon PC / Steam | Erre a buildre nincs teljes regresszios bizonyitek |

## Kovetkezo kozos proba

Mindket gepen az uj syncfix.11 CMD fusson, majd a jatekmappaban az ellenorzo.
Egyezo Modernization es kivant egyeb modok szuksegesek. A regi letolto CMD
nem valt automatikusan syncfix.11-re: az uj fajlt kell atadni.

Ellenorizendo: azonos hatter; vendeg jegyzet/celkijeloles; minden rajzeszkoz;
ajto/lampa mindket iranyban; beszerzokartya berakas/kivetel/vasarlas;
jobb/bal agyu hozzarendeles/toltes/loves; scout hang/repules/foto;
visszacsatlakozas host-reset nelkul. A fajlellenorzes nem helyettesiti ezt.

A publikus csomag nem tartalmaz HDR-t, szemelyes mentest, tesztnaplot, jatekot,
generalt interop assemblyt vagy harmadik fel modjainak implementaciojat.
