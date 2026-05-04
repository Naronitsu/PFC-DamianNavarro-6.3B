# PFC Menu Cloud

Cloud-based web application for the ITSFT-606-1620 Programming for the Cloud assignment.

## Live URL

- [https://pfc-web-721810082985.europe-west1.run.app](https://pfc-web-721810082985.europe-west1.run.app)

## Project Summary

The system lets authenticated users upload restaurant menu images, stores them in Google Cloud Storage, and tracks references in Firestore using a hierarchical model:

- `restaurants -> menus -> images`

After upload, the app publishes a Pub/Sub message. A Cloud Function processes the image with Cloud Vision OCR and stores `ocrText` on the related menu. A scheduled processor function then converts OCR text into structured dish items (name/price/currency) and updates restaurant status from `pending` to `ready`/`confirmed`.

The catalog page supports:

- dish search
- price sorting (ascending/descending)
- translation of completed entries via a separate HTTP translation function
- cached translation responses with invalidation on menu updates

## Main Cloud Components

- **Cloud Run**: hosts the web app (`PFC.Web`)
- **Cloud Storage**: stores uploaded menu image files
- **Firestore**: stores restaurants, menus, OCR text, structured items, and image references
- **Pub/Sub**: upload event topic (`menu-uploads-topic`)
- **Cloud Functions**:
  - Vision OCR processor (`pfc-menu-vision`)
  - Translation HTTP function (`pfc-translate`)
  - Scheduled menu processor (`pfc-menu-processor`)
- **Cloud Scheduler**: hourly trigger for menu processing
- **OAuth 2.0 + Secret Manager**: sign-in and secure secret handling

