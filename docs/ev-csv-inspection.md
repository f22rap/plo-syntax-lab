# EV CSV inspection (Node.js connector)

The inspection tools read existing source CSVs without uploading them manually or changing the source, condition sets, catalogs, or frequency calculations. Inspection does not compute action EV differences or losses. The separate analysis tool below requires an explicit EV contract. The Windows standalone Node-free MCP does not advertise these tools yet; the installed Node.js connector must be updated and restarted before they are available remotely.

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

## Confirmed EV analysis (2 or 3 actions)

`research_analyze_action_evs` takes `board`, `actions` (2–3 entries with action_id, path, optional expected_sha256), `ev_definition: action_minus_best_other_bb`, `complete_action_menu: true`, and `common_weight_scale: true`. The operator must confirm the full legal action menu, not merely the files available. Current user context confirms a bb difference to the alternative; three-action interpretation must mean the best OTHER action for each row, not the same global runner-up for every row.

For two actions only, missing EV can be reconstructed as `other_ev = ev - ev_diff`. For three actions, missing EV stays missing. Action loss is `max(0, -ev_diff)`; with all action EVs present, missing diffs can instead be evaluated relative to the highest EV. Fully observed values are cross-checked against the definition. Partial rows are checked for contradictions between their implied best values and known alternatives. Contradictory hands contribute to coverage denominators but not loss numerators.

Default residual tolerance is 0.015bb for independently rounded 0.01bb EV and diff columns. It is configurable, recorded, and is not a solver-convergence claim. Calculations currently use floating point; raw strings stay available through inspection. Near-zero values should not be interpreted more precisely than source rounding.

Each action summary reports covered range weight and mean loss on that covered range, weighted by the sum of action weights for each hand. This describes always choosing that action on covered hands. It is not the loss of the observed strategy, an optimized simplified strategy, or bb/100. Different coverage can make two summary means incomparable. Results cover all CSV hands, not a selected top-pair syntax. GUI integration, syntax-condition aggregation and stored EV profiles are subsequent work. This read-only tool does not alter existing saved frequency/EQ results.

AT7 live-data precheck: the first 200 matched CO hands and first 200 matched BTN hands agreed with the two-action difference definition to floating-point precision. This is a sorted sample, not verification of all hands and not validation of three-action exports.

### Board-specific condition aggregation

`research_analyze_action_evs` accepts an optional `conditions` array (up to 100), each with `condition_id`, `label`, and exact board-resolved `syntax`. It uses the Lab engine's syntax compiler on physical hands. Resolve saved generated recipes for the requested board before calling; do not reuse another board's literal syntax or silently substitute defaults. This endpoint does not yet resolve saved condition sets automatically or update the GUI.

Each condition reports action frequency using the sum of all action weights as its denominator, per-action EV loss coverage, and `mean_loss_bb_on_common_range` using only the identical hands whose losses are available for every action. Contradictory hands remain in the frequency denominator but are excluded from losses. Missing/zero-weight cohorts have null means, not zero. Conditions can overlap and must not be summed. Top-level statistics and paginated hand items still describe the full node; conditions are aggregate results independent of pagination. These are losses from always choosing an action under the exported continuation EVs, not a re-solved strategy.
