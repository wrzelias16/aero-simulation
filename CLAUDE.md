# Windkanal – Hinweise für Claude

Virtueller Windkanal für Windows in C# (.NET Framework 4.8), Windows-.exe.
Repo: github.com/wrzelias16/aero-simulation (privat). Ausführliche Technik steht in `README.md`.

## Stand
- **v0.0.1** = fertige 2D-Version ("Windkanal 2D"), alles in `main`.
- GPU-Löser (OpenCL, CPU-Fallback), neues UI (Schrift Outfit, Hell/Dunkel, Startfenster), Rauchmodus,
  79 Modelle als Textdateien (`Quellcode/Modelle/LIESMICH.md`, keine echten CAD-Daten), Anzeige "Feinstes Detail".
- Rendering und Rauch-Partikel laufen noch auf der CPU (nächster Perf-Schritt: Bild auf GPU zeichnen, zurückgestellt).
- **Nächstes Ziel: 3D-Version.** Elias will, dass 2D "genau da bleibt, wo es ist".

## Bauen und Testen
- Bauen (ohne SDK, nutzt csc aus .NET Framework 4.8):
  `powershell -ExecutionPolicy Bypass -File Quellcode\build.ps1` -> `Quellcode\bin\Windkanal2D.exe`
- Installer: `Quellcode/Setup` (erzeugt `Windkanal2D-Setup.exe`). Test: `Quellcode/Test/ValidationTest.cs`
  (muss nach Löser-Änderungen weiter dieselben Ergebnisse liefern).
- `*.exe` und `Quellcode/bin/` stehen in `.gitignore` und kommen nicht über GitHub; auf jedem Rechner neu bauen.

## Versionsnummern (Git-Tags)
- v0.0.1 = jetziger 2D-Stand (gesetzt). Danach v0.0.2 bis v0.0.9; v0.0.9 ist die letzte Version vor der stabilen 3D-Version
  (auch v0.0.10 und höher sind erlaubt, falls mehr Zwischenstände nötig sind).
- **v1.0.0** = erste saubere, stabile 3D-Version.
- Vor jedem neuen Tag und vor jedem Push Elias kurz fragen.

## Arbeitsablauf (Git)
- Jede neue Aufgabe auf einem **neuen Branch**, nie direkt auf `main`.
- Auf dem PC des Nutzers bauen und testen, damit Elias die .exe selbst starten kann.
- Fertig = Branch pushen + Pull Request (PR). **Claude merged nicht selbst**, Elias merged auf GitHub.
  Bei gestapelten PRs auf die Merge-Reihenfolge hinweisen.
- Elias arbeitet an zwei Rechnern (Desktop mit RTX 5070 Ti und Laptop), jeweils Ordner `Documents\aero-simulation`.
  Regel: **zu Beginn `git pull`/`git fetch` und `git status` prüfen, am Ende pushen.**
  Unfertige Arbeit darf als Sicherung auf den Branch gepusht werden; sie landet erst mit dem Merge in `main`.
- Mehrere parallele Chats teilen sich einen Ordner: bei paralleler Arbeit eigenen `git worktree` benutzen.
- Alte, gemergte Branches (detail-modelle, detail-rauch, gpu-opencl, neues-design) können liegen bleiben.

## Elias und Kommunikation
- Schreibt Deutsch, kennt sich mit Git/Entwicklung wenig aus. Erklärungen **einfach, ruhig, auf Deutsch, in kleinen Schritten,
  ohne Fachchinesisch**. Begriffe wie Branch, PR, Merge, pull/push wurden schon erklärt, trotzdem kurz in Klartext halten.
- Qualität soll gleich beim ersten Mal hoch sein; Usage/Kosten sparen, nicht unnötig viele Runden.
- Bei Unsicherheit einfach fragen.
- Bildschirmfotos per Screen-Zugriff hat Elias abgelehnt: Vorschaubilder aus dem Zeichencode erzeugen
  (siehe `Quellcode/Test/ModellVorschau.cs`).

## Design
- Kein "AI-Slop": keine generischen Schriften (Segoe UI, Arial, Inter vermeiden), keine violetten Verläufe.
- Eng an Eliasʼ Referenzbilder anlehnen. Aktuelle Schrift: **Outfit** (`Quellcode/Fonts`). Hell- und Dunkelmodus.

## Neuer Rechner (z. B. Laptop)
```powershell
git clone https://github.com/wrzelias16/aero-simulation.git   # vorher: gh auth login
powershell -ExecutionPolicy Bypass -File aero-simulation\Quellcode\build.ps1
```
