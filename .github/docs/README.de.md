<div align="center">

# 🌟 Stelliberty

[![Deutsch](https://img.shields.io/badge/Deutsch-red)](README.de.md)
[![简体中文](https://img.shields.io/badge/简体中文-blue)](README.zh-CN.md)
[![English](https://img.shields.io/badge/English-blue)](../../README.md)
[![日本語](https://img.shields.io/badge/日本語-blue)](README.ja.md)
[![한국어](https://img.shields.io/badge/한국어-blue)](README.ko.md)
[![Français](https://img.shields.io/badge/Français-blue)](README.fr.md)

![Stabile Version](https://img.shields.io/github/v/release/Kindness-Kismet/Stelliberty?style=flat-square&label=Stabile%20Version)![Neueste Version](https://img.shields.io/github/v/tag/Kindness-Kismet/Stelliberty?style=flat-square&label=Neueste%20Version&color=orange)![Flutter](https://img.shields.io/badge/Flutter-3.38%2B-02569B?style=flat-square&logo=flutter)![Rust](https://img.shields.io/badge/Rust-1.91%2B-orange?style=flat-square&logo=rust)![Lizenz](https://img.shields.io/badge/license-Stelliberty-green?style=flat-square)

![Windows](https://img.shields.io/badge/Windows-0078D6?style=flat-square&logo=windows11&logoColor=white)![Linux](https://img.shields.io/badge/Linux-FCC624?style=flat-square&logo=linux&logoColor=black)![macOS](https://img.shields.io/badge/macOS-Experimentell-gray?style=flat-square&logo=apple&logoColor=white)![Android](https://img.shields.io/badge/Android-Nicht_unterstützt-lightgray?style=flat-square&logo=android&logoColor=white)

Ein moderner, plattformübergreifender Clash-Client, entwickelt mit Flutter und Rust.
Er zeichnet sich durch den einzigartigen visuellen Stil **MD3M** (Material Design 3 Modern) aus.

</div>

## 📸 Anwendungs-Screenshots

<table>
  <tr>
    <td width="50%"><img src="../../.github/screenshots/home-page.jpg" alt="Startseite"/></td>
    <td width="50%"><img src="../../.github/screenshots/uwp-loopback-manager.jpg" alt="UWP Loopback Manager"/></td>
  </tr>
  <tr>
    <td align="center"><b>Startseite</b></td>
    <td align="center"><b>UWP Loopback Manager</b></td>
  </tr>
</table>

---

## ✨ Funktionen

- 🎨 **MD3M-Designsystem**: Ein einzigartiger Stil, der das Farbmanagement von Material Design 3 mit Acrylglas-Effekten (Milchglas) kombiniert.
- 🦀 **Rust-Backend**: Ein hochleistungsfähiger Rust-Kern sorgt für die Logik, während die Benutzeroberfläche in Flutter realisiert ist.
- 🌐 **Mehrsprachigkeit**: Internationale Sprachunterstützung ist dank des slang-Frameworks fest integriert.
- 🔧 **Abo-Verwaltung**: Umfassende Unterstützung für Abonnements und Konfigurations-Overrides.
- 📊 **Echtzeit-Monitoring**: Überwachung von Verbindungen und Datenverkehr in Echtzeit.
- 🪟 **Native Desktop-Integration**: Unterstützt Windows-Dienste, System-Tray-Icon und Autostart-Funktionen.
- 🔄 **Integrierter UWP Loopback Manager**: Ermöglicht die Verwaltung von Loopback-Berechtigungen für UWP-Apps (nur Windows).

### 🏆 Implementierungs-Highlights

Diese App ist eine der detailreichsten Desktop-Anwendungen, die mit Flutter umgesetzt wurden:

- ✨ **Adaptives Tray-Icon**: Das Icon in der Taskleiste passt sich automatisch dem hellen oder dunklen Design von Windows an.
- 🚀 **Flimmerfreier Start**: Kein Flackern beim Start der Anwendung, auch nicht im maximierten Zustand.
- 👻 **Weiche Fensterübergänge**: Sanfte Animationen beim Ein- und Ausblenden des Fensters.
- 🎯 **Pixelperfektes UI**: Ein bis ins Detail ausgearbeitetes MD3M-Designsystem.

---

## 📋 Benutzerhandbuch

### Systemanforderungen

- **Windows**: Windows 10/11 (x64 / arm64)
- **Linux**: Gängige Distributionen (x64 / arm64)
- **macOS**: Experimentell

> ⚠️ **Plattform-Support**: Die Anwendung ist für Windows und Linux vollständig getestet. Die Unterstützung für macOS ist experimentell, weshalb einige Funktionen möglicherweise noch nicht einwandfrei laufen.

### Installationsmethoden

**Download-Optionen:**
- **Stabile Version**: [Releases](https://github.com/Kindness-Kismet/stelliberty/releases)
- **Beta-Version**: [Pre-Releases](https://github.com/Kindness-Kismet/stelliberty/releases?q=prerelease%3Atrue) (um die neuesten Funktionen vorab zu testen)

**Installationsmethode (Windows):**

#### Methode 1: Portable Version (ZIP-Archiv)
1. Laden Sie die `.zip`-Datei von der Release-Seite herunter.
2. Entpacken Sie sie an einen beliebigen Ort (z. B. `D:\Stelliberty`).
3. Führen Sie `stelliberty.exe` direkt aus dem entpackten Verzeichnis aus.
4. ✅ Keine Installation erforderlich, sofort einsatzbereit.

#### Methode 2: Installationsprogramm (EXE)
1. Laden Sie das `.exe`-Installationsprogramm von der Release-Seite herunter.
2. Führen Sie das Installationsprogramm aus und folgen Sie dem Assistenten.
3. Wählen Sie einen Installationsort (siehe die unten stehenden Einschränkungen).
4. Starten Sie die Anwendung über die Desktop-Verknüpfung.
5. ✅ Enthält ein Deinstallationsprogramm und eine Desktop-Verknüpfung.

**Einschränkungen für das Installationsverzeichnis:**

Um Sicherheit und Stabilität zu gewährleisten, gelten für das Installationsprogramm die folgenden Einschränkungen für den Installationspfad:

- **Systemlaufwerk (normalerweise C:)**:
  - ✅ Installation erlaubt in: `%LOCALAPPDATA%\Programs\*` (z. B. `C:\Users\Benutzername\AppData\Local\Programs\Stelliberty`)
  - ❌ Installation verboten in: Stammverzeichnis des Systemlaufwerks (z. B. `C:\`)
  - ❌ Installation verboten in: allen anderen Pfaden auf dem Systemlaufwerk
  
- **Andere Laufwerke (D:, E: usw.)**:
  - ✅ Völlig frei, keine Einschränkungen.
  - ✅ Installation im Stammverzeichnis möglich (z. B. `D:\`, `E:\Stelliberty`).

> 💡 **Empfehlung**: Um potenziellen Berechtigungsproblemen vorzubeugen, empfehlen wir die Installation auf einem Laufwerk, das nicht das Systemlaufwerk ist (z. B. `D:\Stelliberty` oder `E:\Programme\Stelliberty`).

> 📌 **Hinweis**: Der Standard-Installationspfad `%LOCALAPPDATA%\Programs\Stelliberty` benötigt keine Administratorrechte und ist für die meisten Nutzer die beste Wahl.

**Installationsmethode (Linux):**

#### Arch Linux (AUR)
Unterstützte Architekturen: `x86_64`, `aarch64`

Mit `yay`:
```bash
yay -S stelliberty-bin
```

Mit `paru`:
```bash
paru -S stelliberty-bin
```

> AUR-Paketlink: [stelliberty-bin](https://aur.archlinux.org/packages/stelliberty-bin)

---

#### Portable Version (ZIP-Archiv)
1. Laden Sie die `.zip`-Datei für Ihre Architektur (`amd64` oder `arm64`) von der Release-Seite herunter.
2. Entpacken Sie sie an einen beliebigen Ort (z. B. `~/Stelliberty`).
3. **Wichtig:** Geben Sie dem Anwendungsordner Berechtigungen:
   ```bash
   chmod 777 -R ./stelliberty
   ```
4. Führen Sie `./stelliberty` direkt aus dem entpackten Verzeichnis aus.
5. ✅ Sofort einsatzbereit.

### Fehler melden

Sollten Sie auf einen Fehler stoßen, folgen Sie bitte diesen Schritten, um ihn zu melden:

1. Aktivieren Sie die **App-Protokollierung** unter **Einstellungen** → **Verhalten der App**.
2. Führen Sie die Aktion aus, die den Fehler verursacht, um einen Protokolleintrag zu erzeugen.
3. Die Protokolldatei finden Sie im Ordner `data` im Installationsverzeichnis der App.
4. Entfernen Sie alle sensiblen oder privaten Daten aus der Protokolldatei.
5. Erstellen Sie ein neues Issue auf GitHub und fügen Sie die bereinigte Protokolldatei hinzu.
6. Beschreiben Sie den Fehler und die Schritte, um ihn zu reproduzieren.

---

## 🛠️ Entwicklerhandbuch

### Voraussetzungen

Bevor Sie dieses Projekt erstellen, stellen Sie sicher, dass die folgenden Tools installiert sind:

- **Flutter SDK** (neueste stabile Version empfohlen, mindestens 3.38)
- **Rust-Toolchain** (neueste stabile Version empfohlen, mindestens 1.91)
- **Dart SDK** (im Lieferumfang von Flutter enthalten)

> 📖 Diese Anleitung setzt grundlegende Kenntnisse in der Flutter- und Rust-Entwicklung voraus. Die Installation der jeweiligen Entwicklungsumgebungen wird hier nicht behandelt.

### Abhängigkeiten installieren

#### 1. Skript-Abhängigkeiten installieren

Das Pre-Build-Skript benötigt zusätzliche Dart-Pakete:

```bash
cd scripts
dart pub get
```

#### 2. rinf CLI installieren

Installieren Sie das Rust-Flutter-Bridge-Tool global:

```bash
cargo install rinf_cli
```

#### 3. Projekt-Abhängigkeiten installieren

```bash
flutter pub get
```

#### 4. Notwendigen Code generieren

Nach der Installation der Abhängigkeiten generieren Sie den Rust-Flutter-Bridge-Code und die Internationalisierungsdateien:

```bash
# Rust-Flutter-Bridge-Code generieren
rinf gen

# Internationalisierungsdateien generieren
dart run slang
```

> 💡 **Wichtig**: Diese Schritte zur Codegenerierung sind vor dem ersten Build des Projekts zwingend erforderlich.

### Projekt erstellen

#### Vorbereitungen für den Build

**Vor dem Erstellen des Projekts muss das Pre-Build-Skript ausgeführt werden:**

```bash
dart run scripts/prebuild.dart
```

**Parameter des Pre-Build-Skripts:**

```bash
# Hilfeinformationen anzeigen
dart run scripts/prebuild.dart --help

# Plattform-Paketierungstools installieren (Windows: Inno Setup, Linux: dpkg/rpm/appimagetool)
dart run scripts/prebuild.dart --installer

# Android-Unterstützung (noch nicht implementiert)
dart run scripts/prebuild.dart --android
```

**Was macht das Pre-Build-Skript?**

1. ✅ Bereinigt die Asset-Verzeichnisse (der `test/`-Ordner wird beibehalten).
2. ✅ Kompiliert den `stelliberty-service` (die ausführbare Datei für den Service-Modus auf Desktop-Systemen).
3. ✅ Kopiert die plattformspezifischen Tray-Icons.
4. ✅ Lädt die aktuelle Mihomo-Core-Binärdatei herunter.
5. ✅ Lädt die GeoIP/GeoSite-Datenbanken herunter.

#### Schneller Build

Kompilieren und paketieren Sie mit dem Build-Skript:

```bash
# Hilfeinformationen anzeigen
dart run scripts/build.dart --help

# Release-Version erstellen (Standard: nur ZIP)
dart run scripts/build.dart

# Gleichzeitig eine Debug-Version erstellen
dart run scripts/build.dart --with-debug

# Gleichzeitig ein Installationspaket erstellen (Windows: ZIP + EXE, Linux: ZIP + DEB/RPM/AppImage)
dart run scripts/build.dart --with-installer

# Nur Installationspaket erstellen, ohne ZIP (Windows: EXE, Linux: DEB/RPM/AppImage)
dart run scripts/build.dart --installer-only

# Vollständiger Build (Release + Debug, einschließlich Installationspaket)
dart run scripts/build.dart --with-debug --with-installer

# Sauberer Build
dart run scripts/build.dart --clean

# Android APK erstellen (nicht unterstützt)
dart run scripts/build.dart --android
```

**Parameter des Build-Skripts:**

| Parameter | Beschreibung |
|------|------|
| `-h, --help` | Hilfeinformationen anzeigen |
| `--with-debug` | Gleichzeitig Release- und Debug-Versionen erstellen |
| `--with-installer` | ZIP + Installationspaket erstellen (Windows: EXE, Linux: DEB/RPM/AppImage) |
| `--installer-only` | Nur Installationspaket erstellen, ohne ZIP |
| `--clean` | `flutter clean` vor dem Build ausführen |
| `--android` | Android APK erstellen (nicht unterstützt) |

**Ausgabeverzeichnis:**

Die erstellten Pakete befinden sich im Verzeichnis `build/packages/`.

#### Bekannte Einschränkungen

⚠️ **Status der Plattformunterstützung**:

- ✅ **Windows**: Vollständig getestet und unterstützt.
- ⚠️ **Linux**: Kernfunktionen sind verfügbar, aber die Systemintegration (Dienst, Autostart) ist nicht verifiziert.
- ⚠️ **macOS**: Kernfunktionen sind verfügbar, aber die Systemintegration ist experimentell.
- ❌ **Android**: Noch nicht implementiert.

⚠️ **Nicht verfügbare Parameter**:

- `--android`: Die Android-Plattform ist noch nicht angepasst.

### Manueller Entwicklungsprozess

#### Rust-Flutter-Bindungen generieren

Nach dem Ändern der Rust-Signal-Strukturen (mit Signal-Attributen):

```bash
rinf gen
```

> 📖 Rinf definiert Nachrichten über Signal-Attribute in Rust-Structs anstelle von `.proto`-Dateien. Weitere Details finden Sie in der [Rinf-Dokumentation](https://rinf.cunarist.com).

#### Internationalisierungsübersetzungen generieren

Nach dem Ändern der Übersetzungsdateien in `lib/i18n/strings/`:

```bash
dart run slang
```

#### Entwicklungs-Build ausführen

```bash
# Zuerst das Pre-Build-Skript ausführen
dart run scripts/prebuild.dart

# Entwicklung starten
flutter run
```

#### Entwicklungstests

Das Projekt verfügt über ein integriertes Test-Framework zum isolierten Testen bestimmter Funktionen:

```bash
# Test für Überschreibungsregeln ausführen (unterstützt YAML- oder JS-Regeln)
flutter run --dart-define=TEST_TYPE=override

# IPC-API-Test ausführen
flutter run --dart-define=TEST_TYPE=ipc-api
```

**Erforderliche Testdateien** befinden sich in `assets/test/`:

- **Für den `override`-Test erforderliche Dateien:**
  ```
  assets/test/
  ├── config/
  │   └── test.yaml          # Basiskonfigurationsdatei für Tests
  ├── override/
  │   ├── your_script.js     # JS-Überschreibungsskript
  │   └── your_rules.yaml    # YAML-Überschreibungsregeln
  └── output/
      └── final.yaml         # Erwartete endgültige Ausgabedatei nach Anwendung der Überschreibungen
  ```

- **Für den `ipc-api`-Test erforderliche Dateien:**
  > **Tipp**: Es wird empfohlen, vor dem Testen das Pre-Build-Skript (`dart run scripts/prebuild.dart`) auszuführen, um die erforderlichen Ressourcen herunterzuladen.
  ```
  assets/test/
  └── config/
      └── test.yaml          # Basiskonfigurationsdatei für Tests
  ```

> 💡 **Hinweis**: Der Testmodus ist nur in Debug-Builds verfügbar und wird in Release-Builds automatisch deaktiviert.

Testimplementierung: `lib/dev_test/` (`override_test.dart`, `ipc_api_test.dart`)

---

## ❓ Fehlerbehebung

### Port wird verwendet (Windows)

Wenn ein Portkonflikt auftritt:

```bash
# 1. Prozess finden, der den Port belegt
netstat -ano | findstr :Portnummer

# 2. Prozess beenden (als Administrator ausführen)
taskkill /F /PID XXX
```

> ⚠️ **Wichtig**: Führen Sie die Eingabeaufforderung als Administrator aus. Der im Service-Modus gestartete Kernprozess kann nur mit erhöhten Rechten beendet werden.

### Software funktioniert nicht ordnungsgemäß

**Pfadanforderungen** (gilt für ZIP und EXE):

- Der Pfad sollte keine Sonderzeichen enthalten (außer Leerzeichen).
- Der Pfad sollte keine Nicht-ASCII-Zeichen (z. B. Umlaute wie ä, ö, ü) enthalten.
- Leerzeichen werden unterstützt: `D:\Program Files\Stelliberty` ✅

**Standortbeschränkungen für das EXE-Installationsprogramm**:

Bei Verwendung des EXE-Installationsprogramms gelten zusätzliche Standortbeschränkungen:

- **Systemlaufwerk (C:)**: Installation nur in `%LOCALAPPDATA%\Programs\*` erlaubt.
- **Andere Laufwerke (D:, E: usw.)**: Keine Einschränkungen.

> 💡 **Tipp**: Falls Sie die Anwendung an einem Ort installieren möchten, den das EXE-Installationsprogramm nicht zulässt, verwenden Sie stattdessen die **portable ZIP-Version**. Diese hat keine Pfadbeschränkungen, kann jedoch durch Systemberechtigungen beeinflusst werden (z. B. wenn Sie sie in `C:\Windows` oder `C:\Program Files` entpacken, was Administratorrechte erfordern kann).

### Fehlende Laufzeitbibliotheken (Windows)

Falls die Anwendung unter Windows nicht startet oder sofort abstürzt, fehlen möglicherweise die erforderlichen Visual C++-Laufzeitbibliotheken.

**Lösung:**

Installieren Sie die Visual C++-Laufzeitbibliotheken: [vcredist - Visual C++ Runtimes All-in-One](https://gitlab.com/stdout12/vcredist)

---

## 🎨 Über das MD3M-Design

**MD3M (Material Design 3 Modern)** ist ein einzigartiges Designsystem, das Folgendes vereint:

- 🎨 **Material Design 3**: Modernes Farbsystem und Typografie.
- 🪟 **Milchglaseffekt**: Halbtransparenter Hintergrund mit Unschärfeeffekt.
- 🌈 **System-Themenintegration**: Automatische Anpassung an die Akzentfarbe des Systems.
- 🌗 **Dunkelmodus-Unterstützung**: Nahtloser Wechsel zwischen hellem und dunklem Thema.

Dies schafft ein modernes, elegantes Desktop-Erlebnis mit einem nativen Gefühl auf allen Plattformen.

---

## 📋 Coderichtlinien

- ✅ Keine Warnungen von `flutter analyze` und `cargo clippy`.
- ✅ Code vor jedem Commit mit `dart format` und `cargo fmt` formatieren.
- ✅ Automatisch generierte Verzeichnisse (`lib/src/bindings/`, `lib/i18n/`) werden nicht manuell geändert.
- ✅ Eine ereignisgesteuerte Architektur wird bevorzugt; übermäßiger Einsatz von `setState` wird vermieden.
- ✅ Rust-Code verwendet `Result<T, E>` für die Fehlerbehandlung; `unwrap()` ist tabu.
- ✅ Dart-Code ist vollständig Null-sicher (null safety).

---

## 📄 Lizenz

Dieses Projekt ist unter der **Stelliberty License (Sternenfreiheits-Lizenz)** lizenziert – Details finden Sie in der [LICENSE](../../LICENSE)-Datei.

**Kurz gesagt**: Sie können mit dieser Software tun, was immer Sie möchten. Es gibt keine Einschränkungen und keine Pflicht zur Namensnennung.

---

<div align="center">

Powered by Flutter & Rust

</div>