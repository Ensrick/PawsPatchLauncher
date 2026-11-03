# Optional AI improvements

## Local replay compatibility fix (2026-10-29)

Native 1.3.72 film playback creates every participant with `SessionPlayer+0xc`
cleared, including recorded bots. Using that flag alone made guarded-structure
avoidance disappear during playback. In the supplied six-player recording,
after fixing independent-owner reconstruction, this caused the next native
film checksum failure at index 258627 (simulation time 428.0625 seconds).

`routing.c` now reads the serialized kingdom AI-profile string from the loaded
WorldCreator only when source kind is 2 and session state is 4 (film). It matches
the internal kingdom ID, requires one unambiguous bounded setup row and a native
film participant, and leaves human/observer paths native. It never changes the
native AI/film flags or starts strategic controllers. Normal matches and saved
games continue using replicated session player types. No persistent identity
cache or extra RNG is involved.

Native layout evidence: WorldCreatorLoad vtable RVA `4FDFE4`, kingdom setup
vector `+14/+18/+1c`, stride `6c`, internal ID `+0`, AI profile `+2c`.
Native world-to-setup serialization at `294E2C` copies kingdom `+24c` into that
profile field. Player film construction at `1622CC` intentionally passes AI=0.
Recorded participant IDs are not kingdom indices or stable bot identifiers.

`test_film_routing.py` compares real compiled route/line-of-sight/cost callbacks
for recording and playback at three image bases, checks malformed/duplicate
metadata, observers, human slots, same-process session transitions and write
bounds. The unchanged source rebuilt byte-identically to both published native
profiles before applying this change; unrelated AI behavior is not updated.

Build: `beta7/build.ps1 -AiPolicy -ExhaustionRecovery` with the other current Arcane features. Paw's Patch 0.4.0 Beta exposes one launcher option, enabled by default, for both the data changes and native AI/exhaustion fixes. The installed module and applied setting must agree before launch. Turning off Paw's Patch disables the option; bulk host bot setup remains a separate built-in Paw's Patch feature.

The current game helper installs 58 guarded native call-site wrappers. Observation/eligibility wrappers preserve their original callees and the existing guarded precondition exceptions. Decision callbacks run on the native AI thread; routing callbacks run inside the native path-query critical section, without OS calls, additional RNG, health or ownership writes. Revision 30 additionally submits the original Disband command for a persistently surplus civilian builder. Recruit eligibility uses native short-lived ResourceVectors on the same simulation thread; final admission remains native. An unchanged floating-point score retains its original 80-bit return. The native payload retains its bounded observation ring; the normal helper no longer drains it to disk or captures periodic AI snapshots.

## Diagnostic recording (local update, 2026-10-16)

Normal helpers no longer create `paws_ai_diagnostics` sessions, decision journals,
economy timelines or periodic actor/city snapshots. `Tick()` is intentionally empty.
Native AI policy installation, callbacks and gameplay are unchanged. Existing
recordings are not deleted. The launcher excludes native `SAI_*.log` files and
old `paws_ai_diagnostics` folders from diagnostic archives; the game's own SAI
logging is separate from this removed helper recorder.

## Current revision 31 (0.4.0-beta.2)

r31 excludes capturable towns from future building sites. r30 adds demand-based civilian builders, distinct settlement assignments,
Undead HP20 readiness, native safe staging and surplus-worker disbanding.
Earlier race-independent militia counting, construction protection, and large
lair restrictions remain. See the final revision section for scope and limits.

### r16 baseline

The revision sections below describe successive experiments; revision 16 supersedes
their one-company clearing fallback, hard danger disks, city-only release and
five-scout limit. Those experiments used the 0.4.0-beta.1 local test identity;
the consolidated release is 0.4.0-beta.2.

The observed settlement camp already had base priority 10024.48 but zero assigned
companies and approximately 5.46e-13 final priority. Raising its base again would
not establish that it received an army. The new callback runs after native
1E4E8C returns inside SelectGoals (call site 1E4807), using that invocation's
sorted goal vector and resource budget. At most once per player per ten simulation
seconds it plans a complete force for the selected known feasible settlement camp.
It counts existing assignments, gathers the nearest eligible idle defenders,
and uses native Ego combat values and the same target/composition factor used by
AttackRegion. Required effective force is at least 1.5 times the larger of current
guard CV and the attack goal's defense estimate. If the complete available group
is insufficient, no extra singleton is dispatched by this policy.

Candidates must pass original target admission, be fully recovered and idle for
ten sampled quiet seconds, guard a valid owned structure and have no current
visible local threat or recent damage. Mines and outposts now qualify alongside
cities. Builders are excluded from this extra combat draft. The nearest sufficient
prefix is revalidated and transferred using original goal remove/add methods;
temporary SA references are held and released exactly as in native transfers.
Native execution subsequently issues movement/attack orders. Active sieges,
combat, recovery and movement are not cancelled. The 45-second peaceful-defense
recall filter remains, with immediate threat override. This policy chooses one
camp at a time and does not guarantee that every known site is reachable or that
an approximate favorable combat value wins a battle.

Diagnostics kind 33 records a complete plan, absent goal, insufficient ready
force, already sufficient force, failed revalidation or incomplete native commit.
Kind 34 records per-company exclusion; kind 35 is emitted only after its actual
assignment matches the clearing goal. Planned CV must not be read as evidence
that the entire force physically reached the target. Each protected object is
scanned for danger once per serialized planning callback, not once per company.

Center-builder travel retains large guard-strength/radius-based regional and
cell cost penalties. Danger alone no longer changes native passability. Long
straight shortcuts through danger request weighted routing, while adjacent legal
steps remain usable for path reconstruction and unavoidable passages. Thus a
detour can win when available, but a single dangerous corridor or an endpoint
inside a guard zone is not made unreachable by this patch. Such a route can still
be unsafe. Human paths, terrain collisions, attack/combat bypass and non-center
builders retain their native behavior.

All twelve hard opening/rusher settlement-search profiles now request three
CloseGoals; the extra peaceful-defense scouting cap is also three. This is a
limit, not a promise that three companies are always assigned. Data changes are
included in the same optional AI module and preserve the original baseline hashes.

Compiled x86 fixtures test group admission, full/insufficient/partial assignment,
recovery and threat gates, mines, scout limits and weighted route cases at three
relocated image bases. Native goal-list bookkeeping and portions of path planning
are stubbed explicitly. Startup, a played match and multiplayer determinism are
separate validation gates; emulation alone does not establish those outcomes.

## Enabled behavior

Mask 1 prevents a Construct candidate from targeting a location where another AI with the same nonzero team parent already has an active, assigned Construct goal. If both already committed, native player-list order chooses one. A matching settlement center already owned by that ally also vetoes the candidate. The veto only lowers an already positive native candidate score to zero. It does not make an illegal candidate legal. Native goal cancellation/reselection handles the result; Repair goals are unaffected. This is not a global reservation manager and does not cancel existing Repair goals.

Team parents were compared with the recorded 3v3 kingdoms: the three participants in each team share a distinct parent. No ownership or saved-kingdom remapping is performed.

The native policy does not change recruitment budgets. The accompanying local data experiment (`prepare_data.py`) restores vanilla insufficient-income thresholds where AW explicitly replaced them with zero, adds economic weight 0.2 to AW-added combat recruitment requests, and restores the settlement influence radius factor from 0.1 to 1.7. The emergency attack bonus and income-based aggression remain unchanged. A resource deficit is acceptable when net gold income after its cost and the new company's upkeep remains healthy. The insufficient-income penalty is a soft candidate penalty, not a recruitment ban.

Mask 2 filters native-eligible recruitment templates by the native limited-resource definitions and their paired capacity resources. It counts both company capacity and kingdom points, including a sovereign pioneer consuming 11 points and units in the default formation. Ordinary resource deficits do not veto candidates. Later native budget/admission checks retain actual customized layouts and reservations. No decisions are cached.

Mask 4 rejects a child-building command if the settlement or its center is removed, the center ownership differs, or its body is missing/dead. The original affordability, placement and siege checks still execute for valid centers. This is a stale-command guard; the reported one-second building flash has not been reproduced yet.

## Local routing and mass-siege experiment (revision 7)

Mask 8 adds dangerous-structure avoidance to AI organization path requests, independent of the current strategic goal. Native company queries carry their leader; the validated ElementComponent parent and current organization leader resolve that query to its company. Other individual formation-member queries remain native. It snapshots living hostile, explored structures with a Denizen component, including capturable guarded buildings and camps without a LairComponent. The native Denizen guard and operational range determine the disk (larger range plus six units of formation clearance); native actor combat value determines the weight. Human queries retain the original behavior. Builders avoid all nonempty threats on ordinary routes. Other organizations avoid threats at least 65% as strong as themselves. An explicitly targeted/ignored structure is excluded for both builders and military organizations. This compares one organization, not the total nearby allied army. It uses the game's approximate combat value, not a damage simulation of every spell or weapon.

The previous r6 runtime trace had zero recognized AI path queries: its direct-organization-only filter rejected the engine's actual leader queries, and its LairComponent check also omitted the observed capturable AW guild. The settlement camp does have that component. The r7 tests now reproduce the leader/Element/organization linkage and capturable structures without that component; the recorded live snapshot independently confirms those offsets and backlinks.

Mask 8 also adjusts already positive native goal scores. With exactly one ordinary city, a legal Construct goal requiring `marker_settlement` receives x3. The nearest affordable-strength known settlement camp within 256 world units of a city receives x4; other optional guarded structures receive x0.15 when such a camp exists. Camp feasibility requires owned military CV at least 1.5 times guard CV; builders are excluded. This is approximate military availability, not proof that all these troops can reach the site. Camps are identified by their required site definition, not a fixed race list. Normal enemy towns, defense and economic aggression are unchanged. The native candidate and construction checks still decide eligibility. Zero/negative goals are never made positive.

Builder combat admission remains native. Revision 11 only restricts reassignment of a recently released, fully recovered company to a peaceful DefendRegion goal for 45 game seconds; actual threats override it. There is no elapsed-time siege penalty: the former 75-second rule was removed at the user's request. Settlement-clearing preferences remain. No orders or active combat states are overwritten. Bounded caches use native player order, full actor IDs and simulation time; they reset on world/kingdom changes and time rollback. Transient defense history is relearned after loading a save; live multiplayer determinism is not yet verified.

Both regional and cell costs increase; native collision/line-of-sight checks reject entering the disk, including smoothing shortcuts. A query starting inside allows outward movement. If all passages are dangerous, native path failure is retained: this revision does not yet request an escort or choose a new strategic goal. Defenders, lair stats, ownership, saves and player commands are not modified. Route contexts are thread-checked and nested queries pass through unchanged. Reporting uses a separate last-completed-query mailbox, sampled by the helper, rather than writing concurrently into the AI decision ring.

`prepare_siege_test.py` adds exact-ID requests for five elite siege companies to each Recruiting block of twelve race hard profiles. Base priority 9000, economic weight 0.2, repeat penalty -1250 (the real native subtraction makes this a bonus). Generic siege requests and their monsters are unchanged. Native availability, upkeep, money and both capacity checks still apply. This raises selection priority; it cannot unlock a missing prerequisite or guarantee a number of companies. The same staging tool explicitly sets the Bolt Master captain's KP upkeep to zero without altering ordinary Maelstrom or Siege Master. The installed Bolt Master had no explicit KP charge beforehand; inheritance or the original screenshot's actual formation is not yet proven to be the cause.

## Reports

`<game>/paws_ai_diagnostics/<timestamp>-<pid>/` contains session metadata, rolling decision JSONL, periodic snapshot JSONL, the first snapshot, status and a `closed.json` shutdown marker with final counters. Revision 6 retains four 4 MiB decision files, four 4 MiB snapshot files, two 4 MiB recruitment/veto files, two 4 MiB compact economy timeline files, and one initial snapshot, below 56 MiB total. Old sessions are not deleted automatically. `totalBytesWritten` is cumulative output, not retained disk usage.

- kind 0: goal type/state/base/final priority.
- kind 1: recruitment preparation and chosen city ID, when present.
- kind 2: construction candidate and before/after priority; counter 20 counts vetoes.
- kind 3: native recruitment requirements result and city ID. Zero means this requirements check passed; it does not promise recruitment. Nonzero is the original engine code, not a guessed explanation.

- kind 4: early recruitment eligibility; zero passes; `4294967295` means native template rejection; otherwise `result-1` is the blocked resource index. For capacity refusals `before=requested`, `after=remaining`, `stateOrCityId=used`, `finalPriority=capacity`.
- kind 5: final planning budget with actual layout; same fields; `4294967294` means an unclassified native refusal.
- kind 6: the native recruit queue-insertion call was executed. This is not completed recruitment.
- kind 7: final queued/direct recruitment validation: 1 passed, 0 refused.
- kind 8: native limited-resource admission: zero passes, otherwise resource index + 1.
- kind 9: child-building lifecycle guard; 0 passed to native checks, 1 invalid settlement, 2 missing center, 3 ownership mismatch, 4 dead/invalid body. City and center IDs occupy the two legacy state/priority fields.

- kind 10: direct company creation through the AI path: 1 succeeded, 0 failed, plus city ID.
- kind 31: adjusted goal score; reason 1 city construction, 2 settlement camp, 3 secondary guarded structure. Before/after are native base scores.
- kind 32 appears only in old revision 7-9 reports: builder rejected for an offensive goal. Revision 10 removes this interception; counter 19 now counts goal adjustments only.

Counters 27, 28, 29 respectively count new diagnostic duplicates, early capacity vetoes and invalid-center vetoes. Actual queue insertions and final validation events bypass deduplication.

Repeated evaluations are sampled every five game seconds; counters count all evaluations. Revision 4 groups priorities by kingdom, goal type, definition, state and positive/nonpositive transition rather than short-lived goal object addresses. A per-game-second budget limits bulk goal-priority and unchanged construction events to 64 each. Recruitment preparation, requirements checks and actual vetoes bypass that bulk budget and have a separate retained file. Counters 21-24 record intentional deduplication; 25-26 record intentional bulk sampling. These are distinct from `dropped`, which counts transport losses. The older 3v3 trace lost 53.23% of detailed ring entries and cannot establish every individual recruitment decision.

Snapshots and compact economy summaries run every ten wall-clock seconds and are explicitly asynchronous; compare their start/end game times. The compact timeline preserves a much longer economic history than full snapshots. Invalid/loading/transient snapshots are rejected and reported, never used to make AI decisions. File errors do not stop the game. Hook installation failures roll back all attempted writes, retaining payload memory if rollback cannot be verified.

## Validation boundaries

Local revision 8 fixes the `log-363` SIM access violation at payload +0x2A09.
The global object registry also contains objects without the GActor component
layout. Before reading actor components, native policy checks the exact
1.3.7.2 KKC_GActor primary vtable and the registry generation. Full-ID lookups
use the same gate. Regression fixtures include a registered object with the
observed 0xEFEFEFEF component poison, a short object ending at an unmapped page,
and a recycled generation. The old payload fails the poison fixture; the fixed
payload passes all routing cases at three ASLR bases.

Native tests mock engine callees and verify relocated calling conventions, output preservation, diagnostic-only write boundaries, sampling, floating-point precision and ally/enemy/reservation cases. Managed tests cover transaction faults and snapshot fixtures. These do not replace observing a full match or verifying multiplayer determinism with multiple clients.

Routing counters: 11 completed queries with known threats, 12 recognized AI queries, 13 line-of-sight rejections, 14 dangerous-cell vetoes, 15/16 changed grid/regional costs, 17 queries with threats, 18 threat-list overflow. The last completed query is sampled into `recruitment-and-vetoes` as `kind="route"`; this is not a complete per-query trace.

## Local settlement clearing at every city count (revision 9)

The feasible settlement-camp score bonus now applies with any positive number of owned cities, including later expansion and space for AW sovereign kingdoms. The chosen camp still must be explored, hostile, alive, within 256 units of an owned city, and at most two-thirds of the non-builder army combat value. Only an already positive native score is multiplied by four; one preferred camp is selected. Optional guarded targets retain the existing relative penalty when such a camp exists. The separate threefold construction bonus remains an opening rule for one city. This revision does not change siege-progress tracking, army assignments, resources, saves, or the builder offense filter.

Native wrapper regressions cover 0, 1, 2, 3 and 8 cities and preserve fog, diplomacy, strength and distance exclusions after three cities.

## Local revision 10: restore native builder combat

Removed the offensive builder-admission hook at RVA 1E280F entirely. The engine again decides whether to assign a builder to attack, defend, explore, repair or construct. Builder path queries also respect the native explicitly targeted/ignored structure, allowing approach to an intended combat target; unrelated guarded structures still affect normal travel. The settlement clearing bonus at every positive city count and the existing stalled-siege policy remain unchanged. Routing emulation covers explicit builder targets, ordinary avoidance and native collision rejection. This is a local test revision, not a published update.

## Revision 11: local peaceful defense release

The SAI frame callback samples active DefendRegion companies every 10 simulation seconds. Full intended roster is checked through Organization.layout slot mapping; unused optional slots are valid, dead selected slots are not. Every present member must be alive and at full effective HP, with full company morale and Sleep/Guard state. The company must remain eligible for 30 sampled quiet seconds. Nearby visible hostile mobile combatants, friendly combat and recorded damage within the last 30 seconds block release. Stationary guarded structures alone do not. Damage observations retain coordinates even if a wall or unit dies before the next sample.

Transfers run only in SelectGoals, after native 1E5127 has finished its ordinary exchange attempt. Both goals must still be active, the source must still own the actor, and native target admission and positive marginal gain must pass. The same virtual remove/add methods, five/four arguments, live budget and recalculation flags as 1E50DE/1E50ED are used. The existing caller holds the actor reference; no external movement orders are sent. The next normal strategic cycle (usually 20 game seconds) performs the actual reassignment, not the sampling callback. One company per bot per ten game seconds may be released; exploration is capped at two concurrently assigned companies. An admitted construction task takes precedence, and native recipes consuming kingdom points are reserved from forced exploration.

The previous 75-second stalled-siege multiplier and its state were deleted entirely. Callback mode 18 now only filters peaceful-defense cooldown, never builder attack/repair/construction admission. Hook guards, disabled option, native refusals, world/ID reuse, missing/wounded members, morale, danger, damage, cooldown, construction and long siege priorities are covered by x86 emulation. Goal list bookkeeping and path planning in these fixtures are explicitly stubbed; game launch validation is separate.

## Revision 12: center-builder travel only
Danger routing now requires an actual BuilderComponent and a resolved BuildActor
target with a settlement type (+430) and marker_settlement (+4E0). The definition
list at +504 is populated by native deferred BuildActor resolution (02A7D0).
This covers ordinary and sovereign centers after template inheritance and excludes
garden/foundation-camp workers, miners and all ordinary armies. The old general
route_builder predicate used for army strength scoring is deliberately unchanged.

The whole routing context is bypassed for active AttackActor/AttackRegion goals,
explicit hostile path targets, or company/member Rampage, Kill, Bombard, Siege,
Chasing or Engage states. Neighboring overlapping guard zones therefore cannot
block an intended attack. Normal center-builder travel still uses discovered,
hostile, guarded structures and their actual defender ranges/combat value.
Native diplomacy, terrain collisions and human paths remain authoritative.

13,131 compiled routing checks and 1,475 peaceful-defense checks pass at three
ASLR bases, including non-center builders, cyclic/empty build lists, member combat,
offensive and nonoffensive assignments. Native goal bookkeeping is partly stubbed.
The separate data-only foundation-placement module applies regardless of AI options.

## Revision 13: ten quiet seconds

The quiet eligibility window and recent-damage window are now 10 simulation
seconds, replacing 30. Sampling remains every 10 seconds per player; at most one
eligible company transfers per player per 10 seconds. The two-scout limit,
native goal admission, full health/roster/morale gates and 45-second return
protection are unchanged. A sampled quiet interval is a minimum eligibility
condition, not a guaranteed order deadline. No siege timer is introduced.

Live revision 12 evidence from the 2026-09-21 match: kind 32, sequence 9334,
time 985.25, company 24029 (engineers) transferred DefendRegion -> Explore,
then was observed with a Construct assignment. Other prolonged DefendRegion
assignments exist, including non-city guarded objects, which this city-specific
release does not cover. This observation confirms a real transfer, not complete
elimination of idle defense or multiplayer validation of revision 13.

## Revision 14: five opening close-exploration tasks

All twelve hard profiles request five CloseGoals instead of two in their
opening or rusher settlement-search Ego. Only one num_goals value changes per
profile. Main-path/rusher attack CloseGoals, DeepGoals, goal priorities,
recruitment and economy remain unchanged. prepare_exploration_test.py stages
the narrow data diff; the AI data manifest retains the original baseline hashes.

The custom peaceful-defense release cap also increases from two concurrently
assigned exploration companies to five. It does not create exploration goals
or force five companies out: native admission, available tasks, full recovery,
threat checks, reserved kingdom-point companies and 10/10/45 timings still apply.
The native scout-cap tests cover admission with zero through six existing scouts,
including allowing the fifth and rejecting a sixth at three relocated bases.

## Revision 15: prefer feasible settlement clearing during peaceful release

The native SelectGoals transfer fallback may now hand a full, idle defender to
the selected feasible settlement-camp Attack/Capture goal. The candidate must be
active and admit the company using its original virtual admission and gain
checks. A feasible admitted clearing goal blocks extra scouting for this company;
unknown, overly strong, dead or inaccessible-by-native-admission candidates do
not. Native exploration selection for other companies remains intact. Builders
retain native combat admission and are not drafted by this extra release.

The 10-second quiet/readiness and per-bot dispatch rules, 45-second peaceful
defense recall protection, immediate danger override, five-scout cap and existing
priority score multipliers remain. There is no timed siege cancellation. This
does not prove that every army can reach a target or that all quiet city defense
has been eliminated. The compiled wrapper tests stub goal bookkeeping explicitly.

## Revision 17: capturable outposts require a second city

With improvements enabled (mask 8), an AttackActor/AttackRegion target is
ineligible while its AI owner has fewer than two living owned settlement-site
cities if the target is a living hostile structure with the native
`BodyComponent.captureable` byte set. The definition parser writes that field
at +290 (RVA 027046); the rule does not use a list of building names and does
not require defenders to be present. Settlement-site targets are excluded so
clearing a camp and obtaining an ordinary city remain possible. Foundation
enclaves remain optional capturable structures and do not count as a second
city. Sovereign centers built on settlement sites do count.

The existing goal-score callback sets positive base scores to zero; the
existing actor-admission callback also rejects assignments/reinforcements.
Native goal selection handles reevaluation of existing plans. No direct orders,
combat-state changes, army list edits or elapsed-time siege cancellation are
added. Native refusals, nonpositive scores, self/allied targets, ordinary city
targets and the improvements-off option retain their behavior. The count uses
current owned valid centers on every check, so losing the second city restores
the restriction and loading a save needs no new persistent state.

Diagnostics kind 31 reason 4 records a priority veto; kind 36 records an actor
admission veto. The serialized event layout and 31 hook sites are unchanged.

## Revision 18: recruit idle defenders into pending exploration

An empty Explore goal remained in native state 1, while the peaceful-defense
exchange hook only visited state-2 goals. Increasing the goal count could leave
three requested tasks but only one moving scout. Recorded at 172–182 seconds in
the 20260921-192857-19476 session: pioneer exploring, swordsman assigned Explore
but still recruiting, bowman and pikeman on city defense.

The new 32nd hook wraps RVA 1E4779 after native initial goal recruitment returns.
For a still-pending, empty, positive Explore goal in the current sorted vector,
it chooses one fully recovered, quiet defender by native admission/gain (actor
ID breaks ties). It preserves construction/settlement-clearing reservations,
kingdom-point exclusions, real threats and the per-player 10-second dispatch
interval. Both staged and active Explore assignments count toward the cap of 3.

Source removal uses the native live budget; pending AddActor has null budget
and no recalculation, matching RVA 1E4DF0. Original caller 1E4736 then performs
the normal score calculation, activation/rejection and resource accounting.
The hook never writes goal state or issues commands. Its SA reference is held
across removal/addition and released; a rejected add restores the original goal.
The payload data layout is unchanged. All behavior remains behind AI mask bit 8.

Diagnostics kind 37 records per-company refusal (source, readiness, reserved KP,
construction, clearing, danger, native admission/gain). Kind 38 result 1 records
a staged assignment, which must be followed by a snapshot to establish actual
activation; results 2/3 describe failed-add restoration/unexpected association.

`test_scouting.py` exercises relocated compiled wrappers and the original native
activation caller, including rejection, resource-call flags, reference balance,
pending scout counts and safety gates. Other native services are explicit stubs;
this suite is not a played-match or multiplayer compatibility claim.
`test_opening_capture.py` executes both compiled wrappers at three ASLR bases,
including ownership, center death, recycled IDs, duplicate records, enclaves,
unguarded buildings, option toggling and count transitions. Native scoring and
diplomacy boundaries are stubbed; a new match remains a separate runtime check.

## Revision 19: reserve complete settlement-clearing groups before exploration

The r18 pending Explore callback could take a quiet defender immediately before
the clearing planner tried to assemble a group. The old reservation also required
an active attack and positive singleton marginal gain, although two companies
together could satisfy the native force requirement. This caused competing task
assignments in the 20260921-202113-32604 session at 583 and 683 game seconds.

One read-only group planner now supplies both reservation and assignment. It
considers the preferred known settlement camp, existing attackers, healthy quiet
defenders and healthy explorers outside combat. Native whole-group combat value,
siege factor, admission, actual threats and distance are retained. Only enough
nearest companies are reserved; spare companies may explore, and an insufficient
group creates no permanent reserve. Individual zero gain does not veto a group.

The existing pending hook tries clearing before exploration. Active attacks use
native resource bookkeeping; pending attacks receive the complete group only in
their own activation callback, with the original null-budget/no-recalculation
AddActor flags. The original parent still activates or rejects the task. A failed
group addition removes newly assigned members and attempts to restore their
original tasks; references are held throughout. Existing attackers are untouched.

Diagnostics kind 33 distinguishes planning, active assignment, staged pending
assignment and rollback; kind 35 distinguishes active/staged individual transfers;
kind 39 records complete-group reservations, target and required/available force.
No new persistent state, hook sites, timing or option flags are introduced.
The cap remains three scouts and the calm dispatch interval ten game seconds.

`test_clearing_priority.py` covers the reported zero-singleton-gain conflict,
pending reservations, spare scouts, explorer readiness, partial rollback and the
original activation parent's accept/reject branches at three relocated bases.
Engine services remain partly stubbed; this is not a played-match verification.

## Revision 21: direct clearing and construction precedence

Explorers can join the existing settlement-clearing plan with a complete roster,
full morale and at least 90% aggregate current/max HP. There is no individual
wound threshold. Combat, retreat, recovery and recent damage still protect the
company from optional reassignment. The selected camp and its actual live
DenizenComponent guard IDs are included through native target force evaluation,
instead of treating those guards' presence as an unrelated threat. Another
hostile actor with the same neutral owner remains a separate threat.

When ready force is insufficient, an admissible native DefendRegion goal may
stage it near the camp, outside cities and explored guarded-structure radii.
Selection uses the existing sorted goal vector, native admission/gain and native
membership/refcount bookkeeping. No new goal, tactical state or coordinates are
written. If no safe existing point is available, exploration remains available.
Recovered reinforcements can complete the group directly; pending goals still
require native activation. Save loading can rederive a staging point without
sidecar history. This is restricted to marker_settlement targets; foundation
camps and map generation are not extended by this revision.

Available ConstructSettlement goals can reclaim healthy, non-fighting builders
from optional Attack, Explore or quiet Defense tasks. Native builder capability,
a real free settlement marker and a fresh native construction score are required.
Conversely, a valid assigned construction is protected from these optional
tasks. Recovery and combat retain their existing admission rules. There is no
blanket worker combat ban, no new kingdom-vs-city priority and no siege timeout.

The 12 opening profiles (initial/rusher_need_settlements, two per race) change
think_frequency from 20 to 10 game seconds. Other modes keep their old explicit
or inherited interval. prepare_think_interval.py performs this idempotent data
change and maintains the reversible module manifest. New code remains under
AI mask bit 8, using the existing 33 hooks. Diagnostic kinds 41, 42 and 43 record
construction protection, builder reassignment and staging respectively.

test_builder_clearing.py and test_clearing_rally.py exercise the compiled payload
at three relocated bases, including aggregate health, exact guard membership,
construction availability, recovery exclusions, admission refusal, rollback,
safe staging and reconstruction without runtime history. Native game services
are stubbed; these are not multiplayer or complete played-match acceptance.


## r22: settlement expansion, capacity recovery and kingdom spending

Builders use the same full-roster, >=90% aggregate HP criterion as scouts;
there is no individual-member HP cutoff. Combat and recovery remain native.
The original region evaluation at 5B738 now substitutes own CV=1 only for the
empty enemy=0/own=0 ratio. This removes that reproducible NaN source without
turning arbitrary invalid construction scores into positive candidates.

All feasible known settlement camps receive the attack score preference. The
clearing planner tries successive native goals instead of stopping at a camp
with no goal, rejected candidates or an already complete force. A remembered
camp without a positive native attack goal can prefer its native eligible
scouting region at 1D48E3. Zero/ineligible regions, fog, connectivity, native RNG
and foundation camps remain outside this preference.

Economy reservations exist only with a live vacant settlement marker accepted
by the native construction evaluator and no matching builder. A recent real
builder recruitment request reserves its required company capacity from other
new recruitment; other hard limits must also permit that builder. If already
at capacity, the native Recruit replacement path can stage one weakest calm
Defend company without builders/heroes/combat/clearing obligations. Actual
stock must cover the builder price, and the replacement must fit every hard
capacity. At least two military companies must exist. Native RemoveActor,
AddActor and SmartPtr handling retain ownership; native Execute sends Disband
and Recruit through normal simulation commands. Revalidation at 1DC3C6 checks
the site, funds, company state and city again. The native per-city recruitment
quota is checked BEFORE disbanding; its hash is read without insertion.

When a ready sovereign builder and safe free site exist, new optional spending
must leave its real discounted construction gold available. Existing kingdom,
active combat/recovery, unavailable sites and already started expenditure do
not create this reserve. No gold, company cap, kingdom cap or ownership is
written. Construction and builder recruitment remain eligible to spend.

The 12 opening modes stay at 10 game seconds. The 24 other modes across the
six races/two profiles explicitly use 20 (including inherited modes). Tactical
intervals and the shared template are unchanged. prepare_think_interval.py is
idempotent and updates reversible module hashes. All r22 behavior uses AI mask
bit 8. There are 41 guarded hooks; diagnostic kinds 44-47 describe slot/gold
reservations, staged native replacement and scouting-region preference.

New regression suites: test_region_ratio.py executes original region math and
preserves an occupied 80-bit x87 stack; test_expansion_fallback.py covers goal
fallback and partial exploration; test_economy.py covers capacity/gold/safety,
late revalidation, balanced references and the original Disband execution
branch. These tests use controlled native engine services/command transport;
they are not a played match or multiplayer/save roundtrip acceptance.

### r23: available clearing forces and final garrison upgrades

Clearing donors require at least 70% aggregate HP over living members and 60%
morale. Missing selected members no longer veto attack; there is no individual
wound threshold. Native actual combat value still determines the required
group. Builder recovery and ordinary scouting-release rules are unchanged.
Existing retreat/recovery assignments remain untouched but do not satisfy the
clearing force requirement, including child Move states with native 2000/4000
recovery flags. Diagnostic 48 identifies these unavailable assigned companies.

Final settlement-center `_militia` upgrades get ActorPriority 100000 in all
36 improved-AI egos (19 racial/faction targets, 114 overrides). Native upgrade
prerequisites and payment remain mandatory. The callback floors only finite
positive Upgrade scores at 100000, and these upgrades are exempt from the
first-kingdom gold reserve. Diagnostic 49 records the priority adjustment.
Prices, intervals and other preferences are unchanged. The profile preparation
script is idempotent and updates the reversible data manifest.

test_clearing_readiness.py covers 70/60 boundaries, partial rosters and returning
attackers; test_militia.py covers all target families, rejection, the gold reserve
and original actor-priority arithmetic. Native engine services are explicit
stubs. These checks do not replace played match/save/network acceptance.

### r24: expansion replacement and builder readiness

When a needed builder cannot be recruited because the company limit is full,
the weakest eligible company may now contain heroes or have an active Explore
goal. Sleep/Guard scouts and scouts travelling in native Move/Explore states
are eligible. Hero membership is no longer a veto; actual disbanding still
uses the original Recruit::Execute Disband command and native CanDisband.
No hero or actor lifecycle is implemented by the patch.

Combat, retreat/recovery (including child Move with 2000/4000 flags), recent
danger, builders and committed clearing forces remain excluded. The current
site, funds, capacity, city quota and candidate are revalidated before the
native command. At least two military companies must exist. Failed native
assignment restores the old task and balances held references, including
Explore goals. This does not disband scouts simply to enforce a scout count.

Construction reclaim, protection and the first-kingdom gold reserve now use
at least 70% aggregate current/max HP of living members. No full roster,
individual HP or morale threshold is required. Combat and native recovery
still take precedence. Other policies retain their existing readiness rules,
including 70% HP/60% morale for settlement clearing. No data profiles, think
intervals, difficulty settings, generation, saves or network formats change.

test_builder_clearing.py and test_economy.py cover the boundaries, partial
rosters, zero morale, hero-bearing travelling scouts, late combat/recovery,
failed reassignment, native Disband dispatch, ABI and mutation limits at three
relocated image bases. Native services/transport are explicit stubs; these
checks do not prove hero lifecycle or a successful played expansion.
## Revision 25: large-lair opening gate and Construct travel protection

Planned attacks on the six Arcane Wars `random_lairlargemonster*` definitions
are deferred while the AI owns fewer than three living settlement centers.
The exact IDs are `active_ice_dragon_lair`, `active_lair_dragon_lair`,
`lair_dark_rift`, `branch_thing_dwelling`, `wyvern_nest`, and
`active_storm_drake_crag`. Sovereign centers count, foundation enclaves and
captured outposts do not. Duplicate/stale/foreign/dead entries cannot unlock
the threshold. An unrecognized city-list layout leaves native behavior intact.

The gate runs before the native region candidate comparison, in offensive
goal scoring and in strategic actor admission. It does not hook tactical
self-defense, monster pursuit, defensive assignments or retreat. Settlement
camps retain their existing clearing preference. Ownership is checked afresh;
no new save fields, timers, company limits or network messages are introduced.
Diagnostics add kind50 (candidate veto), kind31 reason5 (goal veto), and kind36
reason2 (actor admission veto).

The builder reservation also recognizes native `Construct` as a travel state
when the company already owns an active settlement construction goal and the
free marker, capability and positive native construction score remain valid.
The 70% aggregate HP criterion is unchanged, without a morale/full-roster gate.
Actual combat, recent damage, nearby danger and native retreat/recovery still
release the reservation. `Construct` is not a new donor state for reclaiming
builders from unrelated tasks, and `Repair` remains outside this addition.

`test_opening_lairs.py` checks all six IDs, the two/three-city boundary,
sovereign/invalid city entries, fresh state after load, and the original native
candidate-winning comparison in both candidate orders at three ASLR bases.
`test_builder_clearing.py` reproduces the observed Construct-to-Defense theft
and checks safety exceptions. Native services are partly stubbed; these tests
do not establish played-match or multiplayer acceptance.

## Revision 26: settlement clearing route recheck and five scouts

The observed kind34/reason4 refusal was the native AttackRegion regional
route alert check (1D6817). It examines neighboring regions as well as those
on the route; the observed ego alert radius was two regions. Trying another
company already happened, but companies sharing the same area could all fail
this check before any clearing force was assembled.

Only for validated, known, hostile settlement camps and this exact native
method, a refusal now triggers a fresh native regional route query (2648EB).
The fallback retains the native known-hostile-building/CV check (1F582A) on
each traversed region and excludes the destination, as the original does.
Unreachable paths and obstacles on the actual regional path still reject the
candidate; the planner continues with the remaining companies. Native path
nodes are released through 763DC. The native goal cache and ego radius are
unchanged. This is regional validation, not a guarantee of a clear cell path.

The entire force still needs sufficient native combat value, at least 70%
aggregate HP and 60% morale, and no active combat/recovery/local danger.
Builders remain excluded. Every selected company is revalidated before any
transfer, and assignment uses the existing native group/activation machinery.
Other target types, ordinary attacks and the large-lair opening gate retain
their existing behavior. Diagnostic kind51 records clear/blocked/unreachable
route results and the blocking region when available.

Both active/pending exploration release caps are now five. Opening CloseGoals
changes from three to five in all twelve hard profiles, one byte per profile.
Later profile values, planning intervals, generation and difficulty are unchanged.
Five is a maximum, not a requirement to divert five companies from combat.

`test_clearing_route.py` executes the compiled callback, original native
regional threat traversal and path-list cleanup at three relocated bases.
It covers a blocked first company with successful later candidates, actual
route obstructions, failed reachability, native threat semantics, group safety
and a refusal discovered during commit revalidation. Route production and
engine services are explicit stubs; these checks do not prove a played match,
network synchronization or save/load acceptance.

### r27: responsive settlement expansion

The original r27 pass ran after the native player's tactical update; r37 below
moves it to the strategic fiber. At most once per four game seconds it reevaluates existing
regional settlement construction/camp goals in explored settlement-site regions
(including dormant goals) and uses native SelectGoals resource
accounting and activation. Extra activation/exchange is restricted to settlement
expansion; unrelated goals retain their strategic cadence (opening 10 seconds,
later modes 20). No planning is performed by the asynchronous frame observer.
New candidate goals still depend on native region registration. Four seconds is
the throttle; dispatch also depends on the native strategic scheduler reaching
an idle boundary, sufficient force, route, money and native acceptance.

City defenders can transfer to settlement clearing when neither the city nor
its center has the native siege flag 0x00100000. Nearby fights or city damage
without that flag no longer block every donor. The company's own combat,
recovery, recent damage, 70% aggregate HP and 60% morale checks remain. Mine and
regional defense retain the previous threat checks. Builders retain their 70%
aggregate HP gate without a morale requirement and can also be recruited when
they have no current assignment.
The existing 45-second return cooldown uses this same siege/readiness rule for
dispatched clearers, so a peaceful city does not immediately reclaim them at
the next strategic update. Siege, damage, recovery and insufficient readiness
still release that protection. Other scouting cooldowns retain their rules.

### r28: supply recruitment cap and goal-warning presentation

Arcane Wars supply company recipes for Human, Drauga, Gauri and Haroun share a
maximum of two living companies per AI kingdom. Early candidate selection,
budget reservation and both native recruitment validation sites check the cap.
Registered companies count before the strategic actor list refreshes, preventing
independent cities from exceeding the cap in the same simulation tick. Existing
excess companies are not disbanded; dead companies permit replacements. Human
recruitment and individual reinforcement units are unaffected.

Goal-engine overtime warnings still reach the native diagnostic logger every
time. Only screen formatting/display is limited to one warning per 30 real
seconds per world. AI work budgets, synchronization, strategic intervals and
goal behavior remain unchanged. The wrapper preserves the logger ABI and skips
UI work before allocating its temporary string; its guard includes the complete
relocated logger pointer. The new test executes that original caller sequence at
three ASLR bases, as well as supply lifecycle and same-tick recruitment cases.

The r27 builder, clearing and scouting policies are unchanged in this revision.

Optional native recruitment into a settlement camp stops once currently
available assigned effective combat value reaches the existing required force.
Existing attackers and their recovery are not cancelled. Diagnostic kind52
records completed extra passes; kind53 records excess recruitment refusal.
After a successful changed assignment the native ExecuteGoals pass sends its
command using normal affordability checks and the strategic command queue.
Only changed, active expansion goals can execute in this extra pass; existing
unchanged orders are not reissued. Its temporary budget-cascade flag is scoped
and restored around the extra execution pass.
`test_expansion_pulse.py` covers siege versus nearby fighting, company damage,
force admission, four-second scheduling, disabled/invalid states, direct builder
assignment, and native pending activation at three relocated bases. Native
selection/list ownership and external engine services are explicit stubs; live
match, multiplayer and save/load acceptance remain separate checks.

### r29: field recruitment counts and committed settlement sites

Native actor/property counters include local denizens. In the recorded Undead
opening, six local boneweavers were treated as existing settlers: the native
role score was 350 - 6 * 5000 = -29650 even with no mobile builder company.
The same counters feed normal military recruitment and specific recruit requests.

Recruitment now uses a separate count of owned field actors. Native owned actor
registration/removal maintains definition, alias and property counts. Local
workers, militia and denizen organizations are excluded by native actor category
and denizen flag, without race/template name lists. Actual field companies and
their members still count, preserving existing duplicate/composition penalties.
The normal world counters and combat/defense scoring are never modified.

Only Recruit's actor/property scoring call and the specific-recruit request
constructor see the new counts. The scoring scope restores the previous scope
and preserves the native extended floating return. Player construction resets
the mirror before initial actors are registered, including a loaded save that
reuses the world/player addresses. Recorded admission membership handles flags
that change before actor removal. No periodic map scan or engine allocation is
added. Uninitialized or inconsistent mirrors retain native behavior; each of
96 player slots supports 2048 count keys and 4096 registered actors. Kind 55
records native versus field counts when they differ.

Company 49488 also alternated between several settlement sites during planning.
The construction reservation now rejects other Construct goals as well as
optional attack/explore/defense goals. It covers pending activation and active
travel. The existing 70% aggregate HP, valid free site, native suitability and
danger/recovery checks remain in force. Queries for the same goal, retreat,
healing and repair remain allowed; an invalid current site releases the builder.

`test_recruit_counts.py` executes the original native actor/property score with
the compiled wrappers at three ASLR bases; its lifecycle services are mocked.
It covers all six race names, field versus local membership, aliases/properties,
death, conversion, ownership transfer, reconstruction, option/human gates,
fallback, original caller argument delivery, ABI and engine-write boundaries.
`test_builder_clearing.py` covers repeated construction-to-construction attempts,
pending activation, unchanged site, invalidated site and danger/recovery release.
These automated checks do not replace live match/save-load acceptance.

### r30: civilian settlement builder fleet

Known, genuinely vacant settlement markers create worker demand; already staffed
active settlement-camp clearing goals add a forecast site for staging. Registry
IDs, native fog/occupancy/construction scoring, owned field companies and actual
factory recruitment jobs are used. Queued and newly created companies count;
local militia and ordinary citizens do not. Counts are cached for four game
seconds, invalidated by registration/removal and queue/direct recruitment, and
reset by player reconstruction. The native one-settler repeat penalty is lifted
only while this builder type has uncovered demand. Initial and final recruitment
checks cap further hiring; native affordability, capacities and replacement
checks still apply. An ordered or existing first-kingdom builder reserves one
ordinary settlement vacancy; only one sovereign builder is needed.

Construction admission also checks other builders' live pending/active goals by
site coordinates, including another worker on the same goal. Existing accepted
construction retains r29 protection. For the verified Undead nation ID, workers
require 20% aggregate HP of living members; other races keep 70%. Morale and a
full roster are not required. Undead workers can leave a native Recover goal for
valid safe construction without waiting for full healing. Combat, retreat,
invalid bodies and actual local danger still block reassignment.
Once assigned, ready Undead workers keep safe valid construction instead of
being immediately reclaimed by Recover for low morale or incomplete healing.
Below 20% HP, combat, retreat, invalidated sites or local danger release the lock.

All builder companies are excluded from planned AttackRegion/AttackStructure
and military DefendRegion assignments (safe worker staging is the exception),
and both hero target scoring and final AttachHero validation. Native
tactical self-defense and withdrawal remain. Existing heroes are not forcibly
removed by the patch.

A worker without a usable vacant construction site can join an existing native
region-defense goal near an actively staffed settlement-clearing task. The point
must be explored, outside city/camp guard radii plus a 48-unit margin, without
visible local danger; the straight approach must not cross a known camp radius.
Native weighted pathfinding still handles terrain. One worker is staged per
camp. Construction takes precedence when available; cancellation of clearing
releases the waiting reservation. If no native safe point is available, no
staging order is invented. Roaming enemies can still change the situation.

A healthy, idle, safe worker beyond remaining demand becomes eligible for native
Disband after 30 game seconds. Active construction, staging, combat, danger,
inconclusive site scores, or newly restored demand cancel that decision. Queued
commands have a further grace period to prevent duplicate or cascading disbands.
The command uses the original allocator/constructor, SAI envelope and command
transport; no actor deletion, resource refund, HP or ownership is written here.

`test_builder_fleet.py` covers three relocated bases, six races, multiple sites,
queued/cancelled jobs, actors born before SAI registration, militia exclusion,
partial Undead HP20 and other-race HP70, recovery release, civilian offense/hero
vetoes, distinct-site claims, safe staging/rollback/cancellation, and surplus
Disband command ABI/reference balance. It also runs the original native repeat
priority formula. Native world services are stubbed; these are not played-match,
multiplayer or save/load acceptance results.

### r31: capturable towns are not future construction vacancies

The shared forecast/staging camp predicate now rejects definitions whose native
BodyComponent.captureable byte is set. A capturable town can require a settlement
marker just like a destructible lair, but conquering it preserves its city rather
than leaving a vacant site. It no longer causes an extra worker to be hired or
staged. The same predicate revalidates cached sites after captureability changes.
Actual free markers and destructible settlement camps retain r30 behavior.

The compiled builder-fleet fixtures cover all six nations, both region/structure
attack goals, a cached target becoming capturable, worker staging, and a separate
real vacancy beside a capturable town. Native services remain stubbed; this is
not a claim of played-match or multiplayer acceptance.

### Arcane city plans r1 (data, native AI remains r31)

`prepare_city_plans.py` adds the racial quarry first in the 18 existing city
plans, including plans shared by second personalities. All 12 hard profiles
also receive a first-copy quarry bonus to unlock Arcane sovereign builders.
One developed military city per profile supplies seven distinct building slots;
it becomes eligible at two owned settlements and gold income of at least 20.
Humans include the library-to-mage-college chain; Shadow and Undead include a
mana focus instead of a market so the plan fits seven slots. Existing economic
plans keep their other buildings. Mismatch demolition stays disabled.

Separate sovereign variants receive first-copy bonuses in non-emergency modes.
Native settlement compatibility, prerequisite chains, affordability and slot
limits choose valid construction targets. Emergency modes retain the quarry
unlock but do not receive the new military city plan. Existing recruiting,
research, resource management, goal priorities and personality filters are
preserved. This changes construction priorities; it cannot replace buildings
in an already full incompatible city without a free slot.

`test_city_plans.py` checks six racial dependency sets, 12 profiles and shared
references, phase/instance gates, emergency exclusions, strategy preservation
and transformation idempotency. Save loading and UI were smoke-tested; the
loaded match's only major AI had no remaining cities or companies, so that
match does not validate all six races' actual construction progression.


## Revision 34 (included in 0.4.0-beta.3)

Allied settlement reservations are disabled: each bot plans its own workers and
uses native occupancy/feasibility. Own builders still reserve distinct sites.
Haroun keeps one reusable ordinary settlement company, including queued hires.
Haroun and Undead need one registered living unit with settlement BuildActor
capability and positive finite HP; a surviving captain alone is insufficient.
Other races retain the 70% aggregate HP rule. Morale/full rosters are not required.

Revision 34 let a full army replace one safe non-hero field company with a recruit whose
native full combat value is at least 35% higher. Both hard resource capacities,
affordable gold, city quota, factory prerequisites, a second remaining military
company, and current danger are checked before the native Disband branch.
Militia, builders and supply companies are excluded. The revision 34 hero exclusion
is superseded by revision 35 below. Kind58 means
staged replacement, not proof of completed hire. Normal native recruitment and
its failure handling retain responsibility for the resulting command.

In the expansion pass, every 60 game seconds a bot with at least five more
cities than an ally may give one non-sovereign, non-besieged city. Choose the ally
with fewest valid cities (humans included), then the transferable city with
fewest container buildings. Execute the same TeamCommand GIVE_ACTOR constructor,
validation and command transport as native SAIGoalGiveActor, with no ownership
writes. Pending commands are not retried for 120 seconds; changed counts are
checked again. Kind59 records queued command, not completed transfer. Caches
reset on world/time changes and native player creation; save formats unchanged.

The r38 correction documented in `docs/city-sharing-live-20260926.md`
targets the current central building in GIVE_ACTOR. Settlement-container IDs
lack the native selectable-target flag and were rejected in beta.6. Pending
tracking still uses the settlement ID across center upgrades.

These native policy changes require Bot improvements. Regression fixtures run
the compiled code at three image bases with controlled native services; they
are not a substitute for live multiplayer acceptance.

## Revision 35 (included in 0.4.0-beta.3)

Keep a selected military replacement assigned while its current safety,
funds, prerequisites and capacity checks still pass. The four-second AI pulse
retries this existing Recruit reservation through native selection, execution
budgeting and final Disband validation. Combat, recovery, lost funds or
prerequisites release the reservation; regular recruitment retains its normal
cadence. Kind60 records final replacement validation, not completed recruitment.

Initial civilian-company recruitment also excludes CharacterComponent hero
candidates before native scoring and pricing. This closes the initial-formation
path which bypassed the later hero-attachment checks. The native captain
fallback remains available; existing saved companies are not rewritten.
Kind61 records an initial builder hero exclusion. Military replacement may
disband companies with heroes: the ordinary game command handles their return
to the kingdom's pool. No direct hero, company or roster mutation is added.

`test_upgrade_completion.py` exercises the compiled filter and reservation at
three image bases, including original native affordability/debit and Disband
code. Engine selection/command transport are controlled stubs, so this test
does not by itself demonstrate the replacement company's creation in a match.

## Revision 36 (0.4.0-beta.4)

Shared path queries identify bots using replicated session player records,
never host-only strategic controllers or goals. Explicit hostile targets and
native combat states retain the attack bypass on every peer. Builder and supply
recruitment limits run in host selection and final AI budget admission; common
queued/direct command execution preserves the native admission result.

`test_peer_parity.py` compares compiled host/client behavior with absent local AI
controllers and different strategic goals. Native service stubs are explicit.
See `docs/desync-audit-20260925.md` for the audit scope and multiplayer acceptance
limits; the first desync in the supplied match is not conclusively attributed.

## Revision 37 (0.4.0-beta.6)

Move mode 34 from tactical player call site `1EF0D9` to the completed strategic
scheduler iteration at `1E1B87`. The native selector invokes strategic yield
`1E156B`; using it from the tactical fiber left `sai+69` set during world ticks
and subsequently set `sai+68` on tactical resume. The native synchronizer then
discarded world records on the host. It also compared the clock against a stale
strategic deadline, producing enormous apparent goal-engine overruns.

The extra pass requires an idle strategic fiber. It respects a suspended
tactical player's reservation and publishes/restores the native strategic
player index and busy flag around each pulse, including every native yield.
Four-game-second throttling, settlement rules and native network commands stay
in place. No sync checks are disabled or checksum state overwritten.

`test_fiber_sync.py` executes original 1372 strategic/tactical yields, native
synchronizer guard and scheduler under three relocated image bases. It
reproduces the former missing-record/flag leak, rejects invalid contexts and
checks checksum retention, player exclusion and cdecl stack/argument integrity
in the compiled replacement. OS switching and engine services are controlled
stubs. See `docs/desync-live-peers-20260925.md` for captured-match evidence and
the still-required two-peer acceptance test.
