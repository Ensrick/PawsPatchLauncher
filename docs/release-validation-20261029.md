# Company controls and launcher release validation - 2026-10-29

Launcher 1.0.6; Arcane Wars 0.4.2-beta.10; Vanilla/Immortals 0.4.1-beta.12.

- Full launcher regression: 38,182 checks; company options: 348; isolated company settings UI: 10.
- Native emitted-code execution: 2,544 checks; retreat transaction: 138; exhaustion transaction: 173; installed options: 72 per profile.
- Native resources preserved in all twelve helpers; feature manifests checked against their preceding releases.
- 21 installation/rollback cases plus four final Arcane powers-on, powers-off, rollback and patch-disabled cases. The optional powers overlay follows the common UI at priority 950 and retains its Shards node.
- Three complete local two-peer network scenarios. Arcane: client loss, lobby host takeover, new match and save, with ignore-zero-morale enabled on both peers. Pure: manual save copy, client-loss recovery, unsafe replacement-host save rejection and rehosting a client save copy.
- Fresh short recordings completed replay in all three mods with matching runtime/settings and no detected assertion or desynchronization. Arcane loading was continued through the game UI; the other two use the QA UserReady command. Immortals completion was accepted visually at its native End of Film dialog because its buffered-log wait timed out; this is not an automated replay pass. This is not a blanket claim for historical recordings or multi-machine NAT conditions.
- Source and launcher publication CI passed for 3311e77620e11a4b80b5a6a8a7699dce7277b33c. Launcher 1.0.5 was not published because artifact metadata validation correctly rejected the old FileVersion; 1.0.6 aligns all version fields.

Detailed local evidence: outputs/release-company-20261029/validation.json and G:/PawCompanyQA-20261029-postboot-r2. Earlier timed-out replay attempts were QA navigation/pause issues or buffered-log completion timeouts; the final successful replay results are explicitly listed in the validation manifest. A parallel startup was stopped by the commit-memory guard; the final Immortals replay was run sequentially.
