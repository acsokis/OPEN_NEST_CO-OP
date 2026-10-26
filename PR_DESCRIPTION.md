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

Validation: local MelonLoader compilation and 136 automated codec/lifecycle/
authority/model, document-fault and real local TCP checks. The updater adds
a read-only path/hash/version checker and explicit mismatch reporting.
Two-PC multiplayer validation is incomplete.
This should be reviewed as a draft, not a verified stable release. Calculator
card closure, saved-map restoration, and BepInEx compatibility still need
runtime validation as detailed in the review document.

Syncfix.11 also selects the host's exact native MissionID rather than the first
card sharing its Unity scene. Matching background maps were confirmed by the
tester in two complete game instances on one PC. Other .11 changes include
document acknowledgements/retries, stable gun routing, guest door/light intents,
native marker endpoints, exact requisition card/slot routing, Modernization map
purchase commands and experimental scout lifecycle/audio replication.

The complete change history since upstream `f49a188` is in [CHANGELOG.md](CHANGELOG.md).
Current verification and limitations are in [docs/SYNCFIX_11_TEST_HU.md](docs/SYNCFIX_11_TEST_HU.md).
The local two-instance test does not establish independent-PC or Steam-relay
coverage, and the background-map confirmation does not resolve every tablet report.
