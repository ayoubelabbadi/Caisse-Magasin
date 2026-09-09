# Caisse — Windows Point of Sale

 Offline Windows point-of-sale application built with C#, WPF, .NET 9, and SQLite. Manage checkout, barcode scanning, inventory, receipts, cashier shifts, partial refunds, sales dashboards, and Excel exports, with PIN-based access for administrators and cashiers.

## Build and run

On Windows with the .NET 9 SDK installed:

```powershell
dotnet run --project src/Caisse.Desktop/Caisse.Desktop.csproj -c Release
```

To build a portable Windows x64 package with the .NET runtime included:

```powershell
.\build-portable.ps1
```

The package is generated at `dist/Caisse-Windows-x64.zip`. Build output and store databases are excluded from this repository. See the French user guide below for setup, operation, backups, and limitations.

## Guide utilisateur



La distribution autonome se trouve dans `dist/Caisse-Windows-x64.zip`. Copier le ZIP sur un PC Windows x64, extraire tout le contenu et double-cliquer sur `Caisse.Desktop.exe`. Le runtime .NET est embarqué : pas de SDK, d'IDE ou d'installation séparée de .NET. Cette distribution est spécifique à Windows x64, pas à macOS/Linux.

Le fichier `Creer un raccourci Bureau.cmd` crée un raccourci vers l'exécutable sur le Bureau. Conserver le dossier à un emplacement permanent avant de créer ce raccourci. Il s'agit d'une application portable, sans installateur ni signature numérique pour le moment.


Les données de chaque PC restent dans `%LOCALAPPDATA%\CaisseMagasin`. Le ZIP ne contient aucune base du magasin. Pour transférer le stock et les ventes, transférer séparément une sauvegarde cohérente et la configuration, application fermée ; aucune synchronisation entre PC n'est incluse.

Reconstruction de cette distribution sur le poste de développement : `./build-portable.ps1`. Le lancement normal ne nécessite pas ce script. Vérification isolée de la distribution : `Caisse.Desktop.exe --verify-installation <dossier-de-test>` ; cette commande vérifie WPF, SQLite, une vente, le ticket et les commandes de fenêtre dans une base distincte.

## Connexion, tactile et shifts

Au premier lancement de cette version, créer le compte **manager** avec un PIN personnel de 6 à 12 chiffres et sa confirmation. Aucun PIN de production par défaut n’est fourni. Le bouton **Employés & accès** permet au manager de créer les comptes caissiers/managers, de réinitialiser un PIN et de régler le seuil de remboursement.

Chaque utilisateur sélectionne son compte et saisit son PIN. Les PIN sont hachés avec PBKDF2-SHA256 et un sel aléatoire ; cinq échecs verrouillent le compte pendant cinq minutes. Un manager connecté peut réinitialiser un PIN. Le bouton **Verrouiller** ferme l’accès à la caisse sans clôturer le shift ; une connexion est nécessaire pour reprendre.

Dans **Mon shift & retours → Session & espèces**, toucher le fond initial puis **Démarrer mon shift**. Le nom vient du compte connecté. Le shift conserve l’identifiant du compte, le nom, la caisse (par défaut le nom de ce PC), l’ouverture, la clôture, le fond et le rapprochement des espèces. Un seul shift peut être ouvert sur cette caisse ; un autre compte ne peut pas encaisser dessus. Un manager peut clôturer un shift bloqué. Toute ancienne session sans compte doit être clôturée par un manager avant un nouveau shift authentifié.

Toucher un champ affiche le clavier intégré : pavé numérique pour montants/quantités/PIN, AZERTY pour les champs texte. Le lecteur de code-barres reste exclu de cette ouverture automatique. Les claviers physiques restent utilisables. **Paiement / Encaisser** affiche les moyens de paiement, le montant donné, **Montant exact**, **20 / 50 / 100 / 200 DH** et la monnaie recalculée. Les raccourcis correspondent à la devise configurée. La carte est validée sur le terminal bancaire externe.

Le tableau de bord présente les ventes par période, un anneau espèces/carte et les ventes par caissier. Toucher une barre affiche son montant ; toucher un caissier sélectionne ses sessions. Les périodes longues sont regroupées en 14 intervalles au maximum. Les nouveaux comptes sont regroupés par identifiant, les anciennes sessions par leur nom historique.

## Remboursements partiels et journal

1. Dans **Gestion → Retours & remboursements**, rechercher puis sélectionner le ticket original.
2. Choisir les quantités article par article. Le solde tient compte des retours précédents. Les remises originales sont réparties au centime ; le cumul ne dépasse jamais le montant payé.
3. Sélectionner un motif prédéfini. **Autre motif** ouvre le clavier AZERTY et exige une explication.
4. Choisir la remise en stock. Le remboursement conserve le moyen de paiement original ; pour une carte, effectuer le remboursement sur le terminal avant de confirmer.
5. Au-delà du seuil, saisir le PIN d’un manager. Le seuil initial est **500 DH**, modifiable dans **Employés & accès** ; 0 exige une autorisation pour tout montant positif. Le contrôle porte sur le cumul des remboursements du ticket pour empêcher le contournement par fractionnement.
6. Le détail du retour et le justificatif conservent ticket, demandeur, manager éventuel, caisse, shift, date, produits, quantités, montant, motif, moyen de remboursement et remise en stock.

Le **Journal** manager conserve ventes, ajouts/retraits du panier, annulations, remises, mouvements d’espèces, retours, changements de catalogue/comptes et ouvertures/clôtures. Annuler un panier avant clôture ou fermeture est également journalisé. Aucune commande de suppression de retours/journal n’est exposée. Des déclencheurs SQLite interdisent leur modification et suppression. Cela ne remplace pas les droits Windows et les sauvegardes : un administrateur du PC disposant d’un accès direct aux fichiers reste hors du périmètre des droits de l’application.

## Mise à jour des données

**Fermer l’ancienne fenêtre avant de relancer `Lancer Caisse.cmd`.** Le lanceur ouvre la distribution de `dist/Caisse-Windows-x64`. Les bases v1 et v2 sont sauvegardées avant migration en schéma v3. Les ventes, retours et sessions historiques sont conservés, sans inventer de compte ni d’autorisation ancienne. Un retour intégral antérieur à cette version reste considéré comme entièrement remboursé.

La sauvegarde quotidienne s’effectue au premier démarrage de la journée, avec une copie de la configuration. Elle reste silencieuse en cas de succès. Conserver aussi des copies sur un autre support. Le ZIP de distribution ne contient pas les données du magasin.

## Démarrer

Double-cliquer sur **Lancer Caisse.cmd**. Si le dossier `publish` est présent, il ouvre directement la version compilée (runtime .NET Desktop 9 requis pour la publication locale). Sinon, il compile et démarre le projet avec le SDK .NET 9.

Pour développer : ouvrir `Caisse.sln` dans Visual Studio/Rider ou lancer depuis ce dossier :

```powershell
.\start.ps1
```

Le premier lancement restaure les paquets NuGet (Internet nécessaire). L'application fonctionne ensuite sans Internet. Pour lancer une compilation existante : `src\Caisse.Desktop\bin\Debug\net9.0-windows\Caisse.Desktop.exe`.

Le catalogue est vide au départ. Ajouter ses produits dans **Catalogue & stock**, ou utiliser le bouton explicite de chargement des exemples. Les exemples ne doivent pas être chargés dans une base destinée à l'exploitation réelle.

## Données lisibles et tableau de bord des caissiers

- **Données** affiche le stock restant dans un tableau. **Exporter les tableaux Excel** crée un classeur `.xlsx` avec tout l’historique : stock actuel, caissiers, ventes, articles vendus, sessions, retours, mouvements d’espèces et journal de stock. Les en-têtes sont figés, les colonnes filtrables, les montants numériques et les codes-barres conservés comme texte pour garder les zéros initiaux. Excel n’a pas besoin d’être installé sur le PC de la caisse pour exporter.
- **Créer une copie de restauration** conserve le format SQLite `.db`, pour récupérer les données dans la caisse. Le classeur Excel est une vue lisible, sans fonction de réimportation. La sauvegarde automatique continue au premier démarrage du jour ; son message technique n’apparaît plus au comptoir. Une erreur de sauvegarde reste signalée.
- **Tableau de bord** affiche les 30 derniers jours par défaut. Choisir une période, Aujourd’hui ou Ce mois, puis sélectionner un caissier pour consulter ses sessions. Les ventes sont comptées après remises ; les retours sont attribués au caissier qui les traite à la date de remboursement, même si un autre caissier a réalisé la vente. Le solde ventes moins retours n’est pas un bénéfice.
- Les nouveaux caissiers sont regroupés par compte authentifié. Les anciens tickets sans session restent « Non attribué ». Les sessions affichées chevauchent la période et les écarts correspondent aux clôtures de cette période.
## Encaissement par lecteur de code-barres

Le comptoir dispose d’un champ de scan dédié, sélectionné au démarrage. Utiliser un lecteur USB en mode clavier, configuré avec un suffixe **Entrée** (ou **Tabulation**). Le code exact enregistré dans le catalogue ajoute immédiatement un article ; chaque nouveau passage augmente la quantité, dans la limite du stock. Les zéros initiaux sont conservés. Le scan fonctionne indépendamment de la catégorie et de la recherche affichées.

**F2** ou toucher le champ de scan remet le curseur dans le champ du lecteur. Le focus y revient après les boutons du panier et la fermeture du ticket. **F3** ouvre la recherche par nom ou référence pour les articles sans code ou une étiquette illisible. Pendant la saisie d’un montant ou d’une recherche, utiliser F2 avant de scanner ; les scans ne sont pas interceptés dans les autres champs ni dans les fenêtres de paiement/gestion.

Un code inconnu affiche un message dans le bloc du lecteur, sans ajouter d’article. Il est possible de rescanner immédiatement ou de rechercher avec F3. Dans le catalogue, laisser le code vide crée une référence interne pour un produit sans code-barres.

Le comptoir affiche 24 produits par page, avec recherche temporisée et navigation précédent/suivant. La correspondance des scans utilise un index en mémoire ; le catalogue et l’historique sont toujours chargés en mémoire au démarrage. Les tests simulent la saisie du lecteur ; vérifier le matériel réel et sa configuration clavier avant utilisation en magasin.

## Fonctionnalités

Les tickets et justificatifs de remboursement disposent d'un aperçu sur fond blanc : nom du magasin centré, articles/quantités/montants alignés, total mis en évidence et montants au format français. L'impression adapte une copie au papier choisi sans modifier l'aperçu. La mise en page étroite est vérifiée par rendu ; le résultat physique dépend du pilote et reste à tester sur l'imprimante du magasin.

- Recherche par nom/référence, scan clavier USB suivi d'Entrée, cartes produits paginées.
- Panier, quantités entières, contrôle du stock, espèces, montant exact et monnaie à rendre.
- Enregistrement carte après validation manuelle sur terminal externe ; aucun débit bancaire effectué par l'application.
- Vente, lignes et mouvements de stock enregistrés dans une transaction SQLite.
- Catalogue : création et modification, code-barres unique, ajustements de stock tracés.
- Historique, aperçu du ticket, impression par le pilote Windows et réimpression sans nouvelle vente.
- Chiffre des ventes du jour, alertes de stock, sauvegarde SQLite cohérente via son API native.

## Architecture

```text
src/Caisse.Desktop         WPF, ViewModels, configuration, aperçu et impression
src/Caisse.Application     Contrat des opérations de stockage et d'encaissement
src/Caisse.Domain          Entités, requêtes de panier et validation monétaire
src/Caisse.Infrastructure  EF Core, SQLite, transactions et sauvegarde
tests/Caisse.Tests         Tests d'intégration avec bases SQLite temporaires
tests/Caisse.Smoke         Parcours ViewModel, chargement WPF et rendu des écrans
```

La première version place l'orchestration transactionnelle dans `SqliteStore`, derrière `IStore`. Les ViewModels n'accèdent pas directement à EF Core. Les montants sont calculés en `decimal` et persistés en centimes entiers ; le prix et le nom vendus sont conservés dans les lignes historiques.

## Données et configuration

Par défaut : `%LOCALAPPDATA%\CaisseMagasin\caisse.db` et `settings.json`. La variable `CAISSE_DATA_DIR` permet d'isoler un autre dossier (tests/démonstration). Ne pas partager le fichier sur un lecteur réseau entre plusieurs caisses.

Dans `settings.json`, application fermée, personnaliser `Name` et `Currency`. Définir la devise avant la première vente : modifier son libellé ne convertit pas les montants historiques. Les dates sont stockées en UTC ; les tickets utilisent l'heure locale.

Sauvegarder depuis l'onglet **Données** vers un autre support. Pour restaurer : fermer toutes les instances, conserver une copie de l'ancien dossier complet, puis placer la sauvegarde dans un dossier de données neuf sous le nom `caisse.db`, avec une copie du `settings.json` correspondant. Démarrer avec `CAISSE_DATA_DIR` pointant vers ce dossier et vérifier catalogue et ventes. Ne jamais remplacer la base pendant que l'application est ouverte.

## Vérifications

```powershell
dotnet test tests/Caisse.Tests/Caisse.Tests.csproj -c Release
dotnet run --project tests/Caisse.Smoke/Caisse.Smoke.csproj -c Release -- artifacts/retail
```

Le smoke test utilise une base distincte et produit des PNG ; il n'imprime rien. Les tests couvrent les montants exacts, les échecs atomiques, le stock, les prix historiques, les entrées invalides et la lecture d'une sauvegarde.

## Limites de cette première version

Pas encore de taxes ventilées, conformité fiscale, clients/fidélité ou synchronisation multi-postes. Les prix sont des prix finaux et les stocks des quantités entières. Les ventes sont conservées sans suppression. Le tiroir-caisse et les imprimantes thermiques doivent être validés sur le matériel réel ; l'impression actuelle utilise le pilote Windows.

La base est créée par EF Core puis mise à niveau par `SchemaUpgrade` dans une transaction SQLite, avec version de schéma `PRAGMA user_version = 3`. Les migrations v1/v2→v3 sont testées avec une ancienne vente et ses lignes. Le catalogue et l'historique sont chargés en mémoire ; prévoir pagination et opérations asynchrones pour de gros volumes.

## Distribution

```powershell
dotnet publish src/Caisse.Desktop/Caisse.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish
```

La publication autonome embarque .NET. Elle requiert le téléchargement des packs runtime et ne constitue pas encore un installateur signé.

## Espaces par rôle

- Caissier : encaissement, ses ventes, ses shifts et les retours. Les tickets des autres shifts restent recherchables pour un remboursement client.
- Administrateur (ancien rôle Manager) : accueil sur le tableau de bord, catalogue et stock, toutes les ventes, données/export, suivi des shifts et retours, rapports et journal. Il peut clôturer un shift bloqué.
- Seul un administrateur peut créer des comptes employés ou administrateurs et réinitialiser les PIN dans **Employés & accès**. Le premier administrateur se crée uniquement lors de la première configuration.
- Les onglets inutiles sont masqués, les historiques personnels sont filtrés et le changement de compte réinitialise la navigation.
