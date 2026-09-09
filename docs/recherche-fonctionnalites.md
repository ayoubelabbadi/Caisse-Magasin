# Fonctions courantes des logiciels de caisse

Recherche effectuée le 7 septembre 2026 à partir des pages officielles de trois éditeurs. Le recoupement permet d'identifier des fonctions récurrentes dans leurs offres ; il ne mesure pas la proportion de magasins qui les utilisent.

| Source | Fonctions décrites utiles à notre projet |
|---|---|
| [Square — Point of Sale](https://squareup.com/us/en/point-of-sale) | Encaissement, tickets, remboursements, suivi du stock, alertes et rapports exportables |
| [Shopify — POS features](https://www.shopify.com/pos/features) | Remises, retours/échanges, stocks et rapports |
| [Odoo 19 — Workflow](https://www.odoo.com/documentation/19.0/applications/sales/point_of_sale/use.html) | Ouverture de caisse, ventes, clients, remises, retours, gestion des espèces et clôture |

## Ajouts livrés dans cette mise à jour

- Sessions : nom du caissier, fond initial, ouverture obligatoire avant encaissement, fermeture avec montant compté et écart enregistré.
- Espèces : entrées et sorties avec motif, historique et calcul de la disponibilité. Les paiements par carte n'augmentent pas le tiroir.
- Remise globale en pourcentage ou en montant ; calcul en décimal, arrondi au centime, enregistrement et impression sur le ticket.
- Retour intégral d'une vente : motif, montant net réellement payé, choix de remise en stock, prévention du double remboursement et justificatif réimprimable.
- Rapports par période : ventes après remises, remboursements à leur date, net, ventilation espèces/carte et export CSV.
- Journal de stock visible : ventes, ajustements et retours ; liste des articles au stock inférieur ou égal à 5.
- Sauvegarde locale quotidienne au premier démarrage du jour, avec copie de la configuration. Cette mesure de fiabilité est un choix du projet, pas une conclusion sur les fonctions des trois éditeurs.
- Mise à niveau de la base précédente sans effacer les ventes, avec sauvegarde avant changement du schéma.

## Fonctions complémentaires à planifier

Retours partiels, échanges, clients et fidélité, commandes fournisseurs, paniers en attente, paiements répartis, rôles et authentification, plusieurs postes et intégration réelle au terminal bancaire. Les taxes et obligations fiscales doivent être définies selon le pays et l'activité avant une utilisation en production.

Le remboursement carte de cette version est un enregistrement comptable après une action sur le terminal externe. Le nom du caissier est une information déclarative, sans contrôle d'accès.
