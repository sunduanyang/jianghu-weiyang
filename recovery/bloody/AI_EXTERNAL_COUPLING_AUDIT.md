# AI startup, round transitions, and spawning audit

Source: supplied 5.0.3 APK, SHA-256 `c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba`. Addresses below are ELF virtual addresses, not metadata RVAs; metadata RVAs are smaller by `0x4000`.

## Startup and time scale

`CharacterEnemyAI.Start` (`0x342a154`) schedules the original equipment-property callback after **0.5 scaled seconds** and the three AI loops after **1 scaled second**. The prefab AI and mover components are enabled. No round-state subscription, opening-phase enable/disable gate, or matching `StateBasedComponentEnabler`/`CharacterInitializer` class was found in the complete own-assembly metadata. Retain the original scaled invokes; do not restart AI at state 4.

The missing external dependency was the UI animation-event relay:

1. MainScene `TimeController` uses `hy`; `hy.OnEnable` (`0x3443788`) subscribes to the local/network state events and `hz.OnTimeScaleRelayed`.
2. `hz.RelayTimeScale(float)` (`0x3443ba0`) forwards the argument unchanged to its static `Action<float>`. Its source script GUID is `3bbf2e95718d2eac197c90705ea09533`.
3. `hy.SetTimeScaleManually` (`0x3442fb4`) sets `Time.timeScale`, changes mixer `SFXPitch`, and emits `OnTimeScaleChanged`. `hy.dxl` (`0x34434f4`, verified against ARM64) restores scale 1 only when the new network state is 3 or 4.
4. MainScene CinematicIntro GameObject 450 has Animator 6713 with `m_UpdateMode: 2` (UnscaledTime), relay 16663, and the original CinematicIntro override controller. Its animation therefore keeps advancing during combat slow motion.

Original `CinematicIntro_Enter` events are `0→2`, `0.083333336→0.5`, `0.1→0.4`, `0.11666667→0.3`, `0.13333334→0.2`, `0.15→0.1`, `3→0.1`. `CinematicIntro_Exit` has `0→0.2`, `0.016666668→0.4`, `0.033333335→0.6`, `0.05→0.8`, `0.06666667→1`, `0.3166666→1`.

Countdown GameObject 382 / Animator 6710 is Normal update mode 0; every Countdown Enter/Exit time event sets scale 1. Round, RoundEnd and MatchEnd animators use UnscaledTime. RoundOrMatchEnd Enter ramps scale 1→0.1 during its first 0.15 seconds, holds through 2 seconds, then restores to 1 by 2.0666666 seconds. The clip has two events at exactly 2 seconds; preserve their source order.

All seven source timelines, event order, and five scene bindings are extracted in `Runtime/Resources/OriginalTimeScaleSource.json` by `scripts/export_bloody_time_events.py`. Continuous integration of the intro timeline gives about **0.475 scaled seconds over its first 3 real seconds**; this explains why the AI's 1-second invoke does not normally elapse during the intro. Exact frame-boundary timing, overlap of Exit/Enter events, and Fusion's scheduling relative to Unity physics still require runtime comparison. The phase timers use Fusion TickTimers and must not be replaced with a clock that is arbitrarily stalled by the intro's combat time scale.

`KienematicDynamicEnabler.OnEnable/dut/hc` (`0x36cc4ac..0x36cc730`) changes only its serialized rigidbody list to Kinematic, yields null **four times**, then changes that list to Dynamic. It is not a six-second opening freeze. None of the three fighter prefabs has this component; its source GUID `7098e91c520bfa0e502d64d0cf7dddda` was also absent from the exported prefabs/scenes. The restored implementation therefore binds actual source entries only and does not freeze all fighter bodies.

The actors each contain one `CharacterAnimationController`. `Start/cef/bv` (`0x341bbcc..0x341bdd4`) repeatedly waits `Random.Range(0,5)` **integer** seconds and assigns Animator speed 1. Although visually redundant, this coroutine consumes the global random stream and must remain alive during the opening sequence. The expression-data exporter replaces existing IDs rather than appending duplicates; current source libraries contain exactly one of these controllers per actor.

## Player slots, positions, and respawning

`GameObject/Runner.prefab`, GUID `fd4b2cda8ed85cd4f951b3377d0f37a2`, is the runner prefab assigned in MainScene. Its parents have identity transforms. SpawnPoint 1, transform fileID 4302127217294661, is **(-1.5, 0.5, 0)**. SpawnPoint 2, transform fileID 4969106005120503, is **(1.5, 0.5, 0)**. AIOpponentSpawner also references point 2.

- `MainNetworkRunner.OnPlayerJoined` (`0x36557a0`) runs on the server, takes the current human fighter dictionary count as the slot, records PlayerRef→slot, then calls `cvy`.
- `cvy` (`0x3655960`) uses playerPrefab1/point1 for slot 0 and playerPrefab2/point2 for nonzero slots, with Quaternion.identity. Its before-spawn closure (`0x3657cb8`, `0x3657d6c`, `0x3657e20`) calls `NetworkPlayer.AssignSlot` (`0x364ddb0`) with the captured slot before registration.
- AIOpponentSpawner `cbf` (`0x341012c`) handles registered slot 0 only in single player. On the server it despawns an existing AI through `cbh`, then `cbg` spawns a dummy for game mode 2 or the enemy prefab otherwise, at spawn point 2 with identity rotation. Its callback `cbe` (`0x34108d0`) assigns AI slot 1. The spawner forces resulting z to 0.
- `DespawnAllFighters` (`0x3655f60`) despawns and clears the human fighter dictionary at offset 0x68 while retaining the PlayerRef→slot dictionary at 0x80 and cached configs at 0x88.
- `RespawnAllFighters` (`0x3656130`) synchronously iterates the retained slot mapping and calls `cvy`. AI respawning follows the player registration event. The old root is not healed or teleported as a substitute.

## Round and death ordering

The mounted rules asset is `MonoBehaviour/MatchRules_MP_3_Friendly.asset`: game mode 5, rounds to win 3, maximum duration 300, intro mode Once (1), **UsesCountdown true (1)**. The class constructor's default false is not the value used by this match.

`Rpc_RequestAdvanceRound` (`0x364aeb8`) only proceeds from state 5. `csu` (`0x3649444`) increments the round and, for rounds after 1, synchronously despawns/respawns fighters before selecting the next phase. Intro EveryRound (2), or Once (1) with round 1, enters state 2 with a 3-second timer. Otherwise it selects countdown state 3 when UsesCountdown is true, or active state 4. Thus this Friendly setup uses intro 3 seconds then countdown 3 seconds for its first round; later rounds start at countdown. These are source phase clocks, separate from scaled AI invokes.

`HandlePlayerDeath` (`0x364a954`) resolves the winner and calls `ctc` synchronously; wins, timer clearing, and state 5/6 happen without an added WaitForSeconds or Invoke. `CharacterVitality.cmu` (`0x350d948`) first disables AI Update, emits the death expression, starts the death-stage coroutine (stage 0 executes immediately), emits the character death event, then calls the game controller. The winner's AI is not stopped merely by state 5/6; its native loops can enter EnjoyingTheVictory.

The current combat integration calls `DisableOriginalAIUpdates()` and then `OriginalExpression.Died()` before starting death stages and publishing `Died`. `OriginalRig.Expressions.cs` has no Died-event subscription, preventing a second expression/voice pulse and duplicate random consumption. `StopOriginalAI()` is for disposal, not the native death disable operation.

After applying a named appearance/config, `ApplyOriginalAppearance` calls `OriginalExpression.Voice.SetVoice(config)` when initialized. This updates gender, VoiceTypeIndex and VoicePitch without replaying Idle or consuming another voice random draw.

## Runtime clip loading correction

Capture 5 exposed a genuine resource dependency: the original one-handed Shield bundle references `2Hands_Stab`, which is not guaranteed to appear in the instantiated prefab's Animation component enumeration. `OriginalHandAnimations.LoadOriginalClips()` now explicitly loads all six original hand clips through `OriginalAIClips.json` before binding all 18 weighted bundles. Missing resources or a source-name mismatch remain errors; no clip is skipped or replaced. `scripts/build_bloody_ai_bundles.py` copies the original .anim files into Resources and preserves duplicate bundle entries.

This audit establishes the source call paths and dependencies described above. It does not establish whole-match physics or full input-replay parity.
