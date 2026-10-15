# EV CSV inspection (Node.js connector)

This first-stage feature reads existing source CSVs without uploading them manually or changing the source, condition sets, catalogs, or frequency calculations. It does not compute action EV differences or losses. The Windows standalone Node-free MCP does not advertise these tools yet; the installed Node.js connector must be updated and restarted before they are available remotely.

## Tools

- `research_inspect_csv`: absolute `path`, physical three-card `board`, optional `expected_sha256`, `offset`, `limit` (1–200). Returns header columns, encoding, SHA-256, row and physical-hand counts, zero-weight counts, field statistics and paginated raw fields.
- `research_match_action_csvs`: `left_path`, `right_path`, `board`; optional `left_sha256`, `right_sha256`, `view` (`all`, `matched`, `left_only`, `right_only`), pagination. Returns both file summaries, overlap/coverage counts and paired original values.

Use only paths under configured inputRoots. Maximum inspection size is the smaller of maxCsvBytes and 32 MiB per file. Supply the returned hashes on later pages to reject changed files. UTF-8 (with/without BOM), BOM-marked UTF-16LE/BE and Shift-JIS fallback are supported. Quoted fields, escaped quotes and quoted newlines are parsed. Headers must be unique and contain exactly one of hand/combo plus weight.

The sample row retains `weight`, `frequency`, `percent`, `ev`, and `ev_diff` as original strings. An absent column is null; an empty source cell stays an empty string. Numeric min/max are approximate JavaScript numbers, not exact-decimal financial calculations. Missing, invalid, duplicate-hand and board-collision issues are reported. Matching refuses files with validation issues. Hand keys canonicalize four distinct physical cards without discarding suits; rows with zero weight are retained. The same source file cannot be paired with itself. Recognized solver filenames must have the requested board and distinct actions for the same player/history. Generic filenames require the operator to establish same-node comparability.

## AT7 workflow

Under the configured COvsBTN/AsTd7c directory inspect these four files separately:

- OOP/donk/AsTd7c_CO_ROOT_C.csv
- OOP/donk/AsTd7c_CO_ROOT_R75.csv
- IP/vs_check/AsTd7c_BTN_C_C.csv
- IP/vs_check/AsTd7c_BTN_C_R50.csv

Match the two CO files with each other, and the two BTN files with each other. Never match CO action EV with BTN action EV. Inspect unmatched rows and confirm whether exports include EV for zero-frequency alternatives. Missing alternatives remain missing, not zero EV.

## Next-stage gate

Record the exporting solver, EV unit (bb/chips/pot fraction), EV reference point, whether ev is the action-specific value, meaning/sign of ev_diff, and whether both actions use compatible continuation assumptions. Column names alone establish none of these facts. Both tools return `ev_semantics: unverified`, `ev_unit: unknown`, and `ev_calculation_performed: false`.

Only after this gate should a later feature compute per-hand bet-minus-check EV and pre-action-weighted conditional means, report comparable weight coverage, distinguish pure from mixed individual strategies, and estimate fixed-continuation action losses. EQ is not EV. A population bb/100 estimate additionally requires node reach probabilities. This stage does not add GUI EV columns or modify saved research result schemas.

Validation: `npm run test:ev-csv` tests physical hand joining, missing columns, precision retention, zero rows, invalid CSVs, source hashes, file boundaries, filename nodes and actual MCP discovery/calls.
