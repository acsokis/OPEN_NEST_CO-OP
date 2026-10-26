Guest sessions can miss map state and calculator printouts, run counter-battery
events independently, and send a local mission failure back to the host. This
experimental branch adds native map initialization and state retries, marker/measurement handling,
calculator-card creation and bidirectional movement, host-controlled counter-battery outcomes, current-game
API fixes, projectile clock preservation, and a default-hidden persistent F8 chat toggle.
The optional Modernization bridge transfers intel/selection/measurements, native
notes, radar contacts, fire plans and pending automatic jobs using matching local
model schemas. Automatic physical execution remains host-controlled. Door and
lamp controls receive host state corrections.

Shared player interactions remain available to every player; host authority is
used for initialization and automatic outcomes, rather than exclusive map editing.

Prepared for @acsokis with AI-assisted implementation. See
docs/SYNCFIX_REVIEW.md for scope, evidence, and known gaps.

Validation: local MelonLoader compilation and 49 codec/lifecycle/authority/model
checks, including the locally installed Modernization assembly. The updater adds
a read-only path/hash/version checker and explicit mismatch reporting.
Two-PC multiplayer validation is incomplete.
This should be reviewed as a draft, not a verified stable release. Calculator
card closure, saved-map restoration, and BepInEx compatibility still need
runtime validation as detailed in the review document.
