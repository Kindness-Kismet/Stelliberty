<div align="center">

# 🌟 Stelliberty

[![Français](https://img.shields.io/badge/Français-red)](README.fr.md)
[![简体中文](https://img.shields.io/badge/简体中文-blue)](README.zh-CN.md)
[![English](https://img.shields.io/badge/English-blue)](../../README.md)
[![日本語](https://img.shields.io/badge/日本語-blue)](README.ja.md)
[![한국어](https://img.shields.io/badge/한국어-blue)](README.ko.md)
[![Deutsch](https://img.shields.io/badge/Deutsch-blue)](README.de.md)

![Version stable](https://img.shields.io/github/v/release/Kindness-Kismet/Stelliberty?style=flat-square&label=Version%20stable)![Dernière version](https://img.shields.io/github/v/tag/Kindness-Kismet/Stelliberty?style=flat-square&label=Derni%C3%A8re%20version&color=orange)![Flutter](https://img.shields.io/badge/Flutter-3.38%2B-02569B?style=flat-square&logo=flutter)![Rust](https://img.shields.io/badge/Rust-1.91%2B-orange?style=flat-square&logo=rust)![Licence](https://img.shields.io/badge/license-Stelliberty-green?style=flat-square)

![Windows](https://img.shields.io/badge/Windows-0078D6?style=flat-square&logo=windows11&logoColor=white)![Linux](https://img.shields.io/badge/Linux-FCC624?style=flat-square&logo=linux&logoColor=black)![macOS](https://img.shields.io/badge/macOS-Expérimental-gray?style=flat-square&logo=apple&logoColor=white)![Android](https://img.shields.io/badge/Android-Non_pris_en_charge-lightgray?style=flat-square&logo=android&logoColor=white)

Un client Clash multiplateforme et moderne, développé avec Flutter et Rust.
Il se distingue par son style visuel unique, le **MD3M** (Material Design 3 Modern).

</div>

## 📸 Captures d'écran de l'application

<table>
  <tr>
    <td width="50%"><img src="../../.github/screenshots/home-page.jpg" alt="Page d'accueil"/></td>
    <td width="50%"><img src="../../.github/screenshots/uwp-loopback-manager.jpg" alt="Gestionnaire de boucle UWP"/></td>
  </tr>
  <tr>
    <td align="center"><b>Page d'accueil</b></td>
    <td align="center"><b>Gestionnaire de boucle UWP</b></td>
  </tr>
</table>

---

## ✨ Fonctionnalités

- 🎨 **Système de design MD3M** : Un style unique qui allie la gestion des couleurs de Material Design 3 à un effet de verre dépoli (acrylique).
- 🦀 **Backend en Rust** : Le cœur de l'application est propulsé par Rust pour des performances optimales, tandis que l'interface est gérée par Flutter.
- 🌐 **Support multilingue** : L'internationalisation est intégrée nativement grâce au framework slang.
- 🔧 **Gestion des abonnements** : Prise en charge complète des abonnements et des configurations de règles de remplacement (overrides).
- 📊 **Monitoring en temps réel** : Suivi des connexions et statistiques sur le trafic réseau.
- 🪟 **Intégration native** : Supporte les services Windows, l'icône dans la barre des tâches et le lancement au démarrage.
- 🔄 **Gestionnaire de boucle locale UWP** : Un outil intégré pour gérer les autorisations de boucle locale des applications UWP (spécifique à Windows).

### 🏆 Points forts de l'implémentation

Cette application se positionne comme l'une des applications de bureau Flutter les plus soignées dans les moindres détails :

- ✨ **Thème adaptatif pour la barre des tâches** : L'icône s'adapte automatiquement aux thèmes clair et sombre de Windows.
- 🚀 **Lancement sans scintillement** : Aucun clignotement visuel au démarrage, même en mode plein écran.
- 👻 **Transitions de fenêtre fluides** : Des animations douces pour afficher ou masquer la fenêtre.
- 🎯 **Interface utilisateur au pixel près** : Un design MD3M méticuleusement conçu pour une finition parfaite.

---

## 📋 Guide de l'utilisateur

### Configuration requise

- **Windows** : Windows 10/11 (x64 / arm64)
- **Linux** : Distributions courantes (x64 / arm64)
- **macOS** : Expérimental

> ⚠️ **Support des plateformes** : L'application est entièrement testée et stable sur Windows et Linux. Le support pour macOS est encore expérimental, et certaines fonctionnalités pourraient être limitées.

### Méthodes d'installation

**Options de téléchargement :**
- **Version stable** : [Releases](https://github.com/Kindness-Kismet/stelliberty/releases)
- **Version Bêta** : [Pré-versions](https://github.com/Kindness-Kismet/stelliberty/releases?q=prerelease%3Atrue) (pour tester les fonctionnalités en avant-première)

**Méthode d'installation (Windows) :**

#### Méthode 1 : Version portable (archive ZIP)
1. Téléchargez le fichier `.zip` depuis la page des versions.
2. Extrayez-le à n'importe quel emplacement (par ex. `D:\Stelliberty`).
3. Exécutez `stelliberty.exe` directement depuis le répertoire extrait.
4. ✅ Aucune installation requise, prêt à l'emploi.

#### Méthode 2 : Programme d'installation (EXE)
1. Téléchargez le programme d'installation `.exe` depuis la page des versions.
2. Exécutez le programme d'installation et suivez l'assistant.
3. Choisissez un emplacement d'installation (voir les restrictions ci-dessous).
4. Lancez l'application depuis le raccourci sur le bureau.
5. ✅ Inclut un programme de désinstallation et un raccourci sur le bureau.

**Restrictions du répertoire d'installation :**

Pour garantir la sécurité et la stabilité, le programme d'installation impose les restrictions suivantes sur le chemin d'installation :

- **Lecteur système (généralement C:)** :
  - ✅ Installation autorisée dans : `%LOCALAPPDATA%\Programs\*` (par ex. `C:\Users\NomUtilisateur\AppData\Local\Programs\Stelliberty`)
  - ❌ Installation interdite à : la racine du lecteur système (par ex. `C:\`)
  - ❌ Installation interdite dans : tous les autres chemins du lecteur système
  
- **Autres lecteurs (D:, E:, etc.)** :
  - ✅ Totalement libre, aucune restriction.
  - ✅ Installation possible à la racine (par ex. `D:\`, `E:\Stelliberty`).

> 💡 **Recommandation** : Pour une expérience optimale et pour éviter tout problème de permissions, nous vous conseillons d'installer l'application sur un disque non-système (par exemple, `D:\Stelliberty` ou `E:\Applications\Stelliberty`).

> 📌 **Remarque** : Le chemin d'installation par défaut, `%LOCALAPPDATA%\Programs\Stelliberty`, ne requiert aucune élévation de privilèges et convient à la majorité des utilisateurs.

**Méthode d'installation (Linux) :**

#### Arch Linux (AUR)
Architectures prises en charge : `x86_64`, `aarch64`

Avec `yay` :
```bash
yay -S stelliberty-bin
```

Avec `paru` :
```bash
paru -S stelliberty-bin
```

> Lien du paquet AUR : [stelliberty-bin](https://aur.archlinux.org/packages/stelliberty-bin)

---

#### Version portable (archive ZIP)
1. Téléchargez le fichier `.zip` pour votre architecture (`amd64` ou `arm64`) depuis la page des versions.
2. Extrayez-le à n'importe quel emplacement (par ex. `~/Stelliberty`).
3. **Important :** Donnez les permissions d'exécution au fichier exécutable :
   ```bash
   chmod +x ./stelliberty
   ```
4. Exécutez `./stelliberty` directement depuis le répertoire extrait.
5. ✅ Prêt à l'emploi.

### Signaler un bug

Si vous rencontrez un problème, veuillez suivre ces étapes pour nous le signaler :

1. Activez la **journalisation de l'application** dans **Paramètres** → **Comportement**.
2. Reproduisez le bug pour qu'il soit consigné dans les journaux.
3. Le fichier journal se trouve dans le dossier `data`, situé dans le répertoire d'installation de l'application.
4. Assurez-vous de supprimer toute information personnelle ou sensible du fichier journal.
5. Ouvrez une nouvelle "Issue" sur GitHub et joignez-y le fichier journal anonymisé.
6. Décrivez précisément le problème et les étapes nécessaires pour le reproduire.

---

## 🛠️ Guide du développeur

### Prérequis

Avant de construire ce projet, assurez-vous que les outils suivants sont installés :

- **SDK Flutter** (dernière version stable recommandée, minimum 3.38)
- **Toolchain Rust** (dernière version stable recommandée, minimum 1.91)
- **SDK Dart** (inclus avec Flutter)

> 📖 Ce guide s'adresse aux développeurs familiarisés avec les écosystèmes Flutter et Rust. Les instructions pour installer ces environnements ne sont pas couvertes ici.

### Installation des dépendances

#### 1. Installer les dépendances du script

Le script de pré-construction nécessite des paquets Dart supplémentaires :

```bash
cd scripts
dart pub get
```

#### 2. Installer rinf CLI

Installez l'outil de pont Rust-Flutter globalement :

```bash
cargo install rinf_cli
```

#### 3. Installer les dépendances du projet

```bash
flutter pub get
```

#### 4. Générer le code nécessaire

Après avoir installé les dépendances, générez le code de pont Rust-Flutter et les fichiers de traduction :

```bash
# Générer le code de pont Rust-Flutter
rinf gen

# Générer les fichiers de traduction
dart run slang
```

> 💡 **Important** : Il est impératif d'exécuter ces commandes de génération de code avant de compiler le projet pour la première fois.

### Construire le projet

#### Préparation avant la construction

**Avant de construire le projet, le script de pré-construction doit être exécuté :**

```bash
dart run scripts/prebuild.dart
```

**Paramètres du script de pré-construction :**

```bash
# Afficher l'aide
dart run scripts/prebuild.dart --help

# Installer les outils de packaging de la plateforme (Windows : Inno Setup, Linux : dpkg/rpm/appimagetool)
dart run scripts/prebuild.dart --installer

# Prise en charge d'Android (non encore implémentée)
dart run scripts/prebuild.dart --android
```

**À quoi sert le script de pré-compilation ?**

1. ✅ Il nettoie les répertoires de ressources (en conservant le dossier `test/`).
2. ✅ Il compile `stelliberty-service` (l'exécutable du mode service pour les plateformes de bureau).
3. ✅ Il copie les icônes de la barre des tâches adaptées à chaque système d'exploitation.
4. ✅ Il télécharge la dernière version du binaire du noyau Mihomo.
5. ✅ Il télécharge les bases de données GeoIP/GeoSite.

#### Construction rapide

Compilez et empaquetez avec le script de construction :

```bash
# Afficher l'aide
dart run scripts/build.dart --help

# Construire la version Release (par défaut : ZIP uniquement)
dart run scripts/build.dart

# Construire également une version Debug
dart run scripts/build.dart --with-debug

# Générer également un paquet d'installation (Windows : ZIP + EXE, Linux : ZIP + DEB/RPM/AppImage)
dart run scripts/build.dart --with-installer

# Générer uniquement le paquet d'installation, sans ZIP (Windows : EXE, Linux : DEB/RPM/AppImage)
dart run scripts/build.dart --installer-only

# Construction complète (Release + Debug, avec paquet d'installation)
dart run scripts/build.dart --with-debug --with-installer

# Construction propre
dart run scripts/build.dart --clean

# Construire l'APK Android (non pris en charge)
dart run scripts/build.dart --android
```

**Paramètres du script de construction :**

| Paramètre | Description |
|------|------|
| `-h, --help` | Afficher l'aide |
| `--with-debug` | Construire les versions Release et Debug en même temps |
| `--with-installer` | Générer ZIP + paquet d'installation (Windows : EXE, Linux : DEB/RPM/AppImage) |
| `--installer-only` | Générer uniquement le paquet d'installation, sans ZIP |
| `--clean` | Exécuter `flutter clean` avant la construction |
| `--android` | Construire l'APK Android (non pris en charge) |

**Emplacement de sortie :**

Les paquets construits se trouveront dans le répertoire `build/packages/`.

#### Limitations connues

⚠️ **État de la prise en charge des plateformes** :

- ✅ **Windows** : Entièrement testé et pris en charge.
- ⚠️ **Linux** : Les fonctionnalités de base sont disponibles, mais l'intégration système (service, démarrage automatique) n'est pas vérifiée.
- ⚠️ **macOS** : Les fonctionnalités de base sont disponibles, mais l'intégration système est expérimentale.
- ❌ **Android** : Non encore implémenté.

⚠️ **Paramètres non disponibles** :

- `--android` : La plateforme Android n'est pas encore adaptée.

### Processus de développement manuel

#### Générer les liaisons Rust-Flutter

Après avoir modifié les structures de signaux Rust (avec des attributs de signal) :

```bash
rinf gen
```

> 📖 Rinf définit les messages via des attributs sur les structures Rust, plutôt qu'avec des fichiers `.proto`. Pour en savoir plus, consultez la [documentation de Rinf](https://rinf.cunarist.com).

#### Générer les traductions

Après avoir modifié les fichiers de traduction dans `lib/i18n/strings/` :

```bash
dart run slang
```

#### Exécuter une construction de développement

```bash
# Exécuter d'abord le script de pré-construction
dart run scripts/prebuild.dart

# Démarrer le développement
flutter run
```

#### Tests de développement

Le projet dispose d'un framework de test intégré pour tester des fonctionnalités spécifiques de manière isolée :

```bash
# Exécuter le test des règles de remplacement (prend en charge les règles YAML ou JS)
flutter run --dart-define=TEST_TYPE=override

# Exécuter le test de l'API IPC
flutter run --dart-define=TEST_TYPE=ipc-api
```

**Fichiers de test requis** situés dans `assets/test/` :

- **Fichiers requis pour le test `override` :**
  ```
  assets/test/
  ├── config/
  │   └── test.yaml          # Fichier de configuration de base pour les tests
  ├── override/
  │   ├── your_script.js     # Script de remplacement JS
  │   └── your_rules.yaml    # Règles de remplacement YAML
  └── output/
      └── final.yaml         # Fichier de sortie final attendu après application des remplacements
  ```

- **Fichiers requis pour le test `ipc-api` :**
  > **Conseil** : Il est recommandé d'exécuter le script de pré-construction (`dart run scripts/prebuild.dart`) avant de tester pour télécharger les ressources nécessaires.
  ```
  assets/test/
  └── config/
      └── test.yaml          # Fichier de configuration de base pour les tests
  ```

> 💡 **Remarque** : Le mode de test est uniquement disponible pour les builds de débogage (Debug) et est automatiquement désactivé pour les builds de production (Release).

Implémentation des tests : `lib/dev_test/` (`override_test.dart`, `ipc_api_test.dart`)

---

## ❓ Dépannage

### Port utilisé (Windows)

Si un conflit de port se produit :

```bash
# 1. Trouver le processus qui occupe le port
netstat -ano | findstr :numéro_de_port

# 2. Tuer le processus (exécuter en tant qu'administrateur)
taskkill /F /PID XXX
```

> ⚠️ **Important** : Cette commande doit être exécutée avec des privilèges d'administrateur. Le processus principal, lorsqu'il est lancé en mode service, ne peut être terminé qu'avec des droits élevés.

### Le logiciel ne fonctionne pas correctement

**Exigences de chemin** (s'applique à la fois au ZIP et à l'EXE) :

- Le chemin ne doit pas contenir de caractères spéciaux (sauf les espaces).
- Le chemin d'accès ne doit pas contenir de caractères non-ASCII (par exemple, des lettres accentuées comme `é`, `à`, `ç`).
- Les espaces sont pris en charge : `D:\Program Files\Stelliberty` ✅

**Restrictions d'emplacement pour le programme d'installation EXE** :

Lors de l'utilisation du programme d'installation EXE, des restrictions d'emplacement supplémentaires s'appliquent :

- **Lecteur système (C:)** : Installation autorisée uniquement dans `%LOCALAPPDATA%\Programs\*`.
- **Autres lecteurs (D:, E:, etc.)** : Aucune restriction.

> 💡 **Conseil** : Si vous souhaitez installer l'application dans un répertoire non autorisé par l'installeur EXE, privilégiez la **version portable (ZIP)**. Celle-ci n'a pas de restrictions de chemin, mais son exécution peut être affectée par les permissions système si elle est placée dans des dossiers protégés (comme `C:\Windows` ou `C:\Program Files`).

### Bibliothèques d'exécution manquantes (Windows)

Si l'application ne se lance pas ou se ferme instantanément sous Windows, il est probable que les bibliothèques d'exécution Visual C++ nécessaires soient manquantes.

**Solution :**

Installez les bibliothèques d'exécution Visual C++ : [vcredist - Runtimes Visual C++ All-in-One](https://gitlab.com/stdout12/vcredist)

---

## 🎨 À propos du design MD3M

**MD3M (Material Design 3 Modern)** est un système de design unique qui fusionne :

- 🎨 **Material Design 3** : Système de couleurs et typographie modernes.
- 🪟 **Effet de verre dépoli** : Arrière-plan semi-transparent avec effet de flou.
- 🌈 **Intégration du thème système** : Adaptation automatique à la couleur d'accentuation du système.
- 🌗 **Prise en charge du mode sombre** : Basculement transparent entre les thèmes clair et sombre.

Cela crée une expérience d'application de bureau moderne et élégante avec une sensation native sur toutes les plateformes.

---

## 📋 Conventions de codage

- ✅ Maintenir zéro avertissement de `flutter analyze` et `cargo clippy`.
- ✅ Formater le code avec `dart format` et `cargo fmt` avant chaque commit.
- ✅ Ne jamais modifier manuellement les répertoires auto-générés (`lib/src/bindings/`, `lib/i18n/`).
- ✅ Privilégier une architecture événementielle et éviter l'abus de `setState`.
- ✅ En Rust, la gestion des erreurs doit se faire avec `Result<T, E>`, l'usage de `unwrap()` est proscrit.
- ✅ Le code Dart doit être entièrement conforme à la null-safety.

---

## 📄 Licence

Ce projet est distribué sous la **Licence Stelliberty (Licence de la Liberté Étoilée)**. Consultez le fichier [LICENSE](../../LICENSE) pour plus de détails.

**En résumé** : Faites ce que vous voulez de ce logiciel. Aucune restriction, aucune attribution requise.

---

<div align="center">

Propulsé par Flutter et Rust.

</div>