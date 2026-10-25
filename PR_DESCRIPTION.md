Guest sessions can miss map state and calculator printouts, run counter-battery
events independently, and send a local mission failure back to the host. This
experimental branch adds map-state retries, marker/measurement handling,
calculator-card creation, host-controlled counter-battery outcomes, current-game
API fixes, projectile clock preservation, and a persistent F8 chat toggle.

Shared player interactions remain available to every player; host authority is
used for initialization and automatic outcomes, rather than exclusive map editing.

Prepared for @acsokis with AI-assisted implementation. See
docs/SYNCFIX_REVIEW.md for scope, evidence, and known gaps.

Validation: local MelonLoader compilation and package hash checks; syncfix.6
startup reached the main menu. Two-PC multiplayer validation is incomplete.
This should be reviewed as a draft, not a verified stable release. Calculator
card manipulation, complete saved-map transfer, and BepInEx compatibility still
need implementation or validation as detailed in the review document.
