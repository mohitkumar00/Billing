# 🎨 BillMate — Modern UI/UX Design System & Frontend Architecture

**Applied Standards:** Senior Frontend Design Guidelines · Material Design 3 (Android) · Fluent Design (Windows)  
**Target Devices:** Windows Desktop (1024px–4K) · Android Tablet (720dp–1024dp) · Android Phone (320dp–600dp)

---

## 1. Visual Identity & Elevated Surface Palette

BillMate adopts a high-contrast, low-fatigue **Slate & Cobalt** design system (Dark Mode default for long counter shifts, with full Light Mode parity), allowing client white-label overrides via `client_config.json`.

### 1.1 Color Tokens

| Token | Dark Theme (Default) | Light Theme | Purpose |
|---|---|---|---|
| `SurfaceBase` | `#09090B` (Zinc 950) | `#F8FAFC` (Slate 50) | App background canvas |
| `SurfaceCard` | `#121216` (Zinc 900/80) | `#FFFFFF` | Bento cards, POS tiles, cart panel |
| `SurfaceElevated` | `#18181B` (Zinc 900) | `#FFFFFF` | Modals, Command Palette, Popovers |
| `BorderSubtle` | `#27272A` (Zinc 800) | `#E2E8F0` (Slate 200) | Hairline 1dp card & table dividers |
| `InnerTopGlow` | `#FFFFFF12` → `#FFFFFF00` | `#FFFFFF80` → `#FFFFFF00` | 1dp top card edge highlight |
| `BrandPrimary` | `#1565C0` *(White-label)* | `#1565C0` *(White-label)* | Primary CTAs, active nav pill, focus rings |
| `BrandAccent` | `#FF6F00` *(White-label)* | `#FF6F00` *(White-label)* | Quick-pay highlights, promotion badges |
| `TextPrimary` | `#FAFAFA` | `#0F172A` | Headings, INR totals, primary labels |
| `TextMuted` | `#A1A1AA` | `#64748B` | Captions, HSN codes, SKU metadata |
| `SemanticSuccess` | `#10B981` / `#10B9811A` | `#059669` / `#ECFDF5` | Paid badge, stock healthy, online sync pill |
| `SemanticWarning` | `#F59E0B` / `#F59E0B1A` | `#D97706` / `#FFFBEB` | Low stock alert, partial payment, offline queue |
| `SemanticDanger` | `#F43F5E` / `#F43F5E1A` | `#E11D48` / `#FFF1F2` | Out-of-stock, validation error, cancelled bill |

### 1.2 Spacing, Radii & Typography
- **8pt / 4px Modular Grid:** `SpaceXS = 4`, `SpaceSM = 8`, `SpaceMD = 12`, `SpaceLG = 16`, `SpaceXL = 24`, `Space2XL = 32`.
- **Corner Radii:** `RadiusCard = 16`, `RadiusButton = 12`, `RadiusChip = 999` (pill).
- **Minimum Touch Target:** `48×48dp` across all POS tiles, qty `+ / -` buttons, and bottom-nav tabs.
- **Tabular Numerals:** All currency values (`₹1,24,500.00`), quantities, GST percentages, and invoice numbers use monospace/tabular font alignment (`HorizontalTextAlignment="End"` in tables) so live totals never jitter horizontally while scanning items.

---

## 2. Mandatory 5 UI States (Every Screen)

Every BillMate ViewModel inherits [`StatefulViewModelBase`](file:///c:/Personal/Billing/src/BillMate.ClientCore/ViewModels/StatefulViewModelBase.cs) exposing explicit state flags bound to reusable UI components:

| State | Component | Behaviour |
|---|---|---|
| **1. Ideal** | Content Grid / `CollectionView` | Crisp Bento cards, formatted INR totals, subtle press scale (`0.98`). |
| **2. Loading** | `SkeletonCardView` / `SkeletonRowView` | Geometry-matched shimmering placeholders (no solitary full-screen spinners). |
| **3. Empty** | `EmptyStateView` | Dashed border container, soft icon badge, helpful subtitle, and direct primary CTA (e.g. *"Create your first product"*). |
| **4. Error** | `ErrorStateView` | Plain-language explanation + domain error code + **Retry** button + offline fallback indicator. |
| **5. Edge / Overflow** | Responsive `VisualStateManager` + `LineBreakMode="TailTruncation"` | Long item/customer names truncate gracefully; narrow 320dp phones reflow grids from 4 cols → 2 cols → 1 col. |

---

## 3. Adaptive Layout Architecture (Windows · Tablet · Phone)

```text
┌────────────────────────────────────────────────────────────────────────────┐
│  WINDOWS DESKTOP & ANDROID TABLET (>= 720dp)                               │
│  ┌───────────┬───────────────────────────────────────────────────────────┐ │
│  │ Sidebar   │  Top Header: [Ctrl+K Command Search]  [🟢 LAN Synced]     │ │
│  │  • Home   ├───────────────────────────────┬───────────────────────────┤ │
│  │  • POS    │  POS Catalog (Grid 3-4 cols)  │  Active Cart (420dp fixed)│ │
│  │  • Bills  │  [Barcode Input (AutoFocus)]  │  • Customer Pill + Credit │ │
│  │  • Stock  │  [All] [Groceries] [Fav ★]    │  • Line Items (+/- / Disc)│ │
│  │  • CRM    │  ┌──────┐ ┌──────┐ ┌──────┐   │  • Taxable + CGST + SGST  │ │
│  │  • Reports│  │Card 1│ │Card 2│ │Card 3│   │  • Hold | Split Pay       │ │
│  │  • Backup │  └──────┘ └──────┘ └──────┘   │  • [Save & Print ₹1,180]  │ │
│  └───────────┴───────────────────────────────┴───────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────┐
│  ANDROID PHONE (< 600dp)    │
│  ┌───────────────────────┐  │
│  │ [Search / Scan 📷]    │  │
│  │ [All] [Fav ★] [Dairy] │  │
│  │ ┌────────┐ ┌────────┐ │  │
│  │ │ Item 1 │ │ Item 2 │ │  │
│  │ └────────┘ └────────┘ │  │
│  │ ┌───────────────────┐ │  │
│  │ │ 3 Items · ₹420 [▶]│ │  │  ← Sticky Cart Summary Pill (opens Sheet)
│  │ └───────────────────┘ │  │
│  ├───────────────────────┤  │
│  │ Home | POS | Bills |≡ │  │  ← Bottom Navigation Bar
│  └───────────────────────┘  │
└─────────────────────────────┘
```

---

## 4. High-Polish Flows Included
1. **Bento Dashboard:** 4-tile asymmetric KPI grid (Today's Sales with trend pill, Cash/UPI/Card split bar, Low-Stock alert card, Sync/Backup health card) + Recent Bills table.
2. **Command Palette (`Ctrl+K` / Search Tap):** Instant fuzzy jump across products, customers, invoices, and quick actions (*New Bill*, *Export GSTR-1*, *Run Backup*).
3. **Quick Cashier PIN Pad + Biometrics:** Large tactile 4–6 digit keypad for fast shift handovers on shared shop terminals.
4. **3-Click Google Drive Restore Wizard:** Stepper progress (`1. Pick Backup` → `2. Download & Verify` → `3. Restore DB`), displaying timestamp IST, size MB, and SHA-256 status.
