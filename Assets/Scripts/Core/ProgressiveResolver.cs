using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

// [SERVER] Generic, game-logic-agnostic pacing loop: mutate one step, then wait, checking an
// abort condition between steps. This is what makes a server-authoritative action resolve
// PROGRESSIVELY — in step with its (future) animation — instead of computing everything at once
// and broadcasting a single final state. That "compute now, animate later" shortcut is exactly
// what AbilityResolver's Calculate/Apply split and BattleController.PlayWithTimeout were already
// scaffolded for (see their doc comments) but never wired up; this is the missing middle piece.
//
// Movement (MoveResolver) is the first consumer — each path tile is one step. AbilityResolver's
// hit-by-hit playback (Calculate → one HitResult per step → Apply) is the intended next one, so
// this stays deliberately ignorant of movement/tiles/fighters — callers own what a "step" means
// and what mutating/broadcasting it looks like.
public static class ProgressiveResolver
{
    /// Runs each step in order: apply it, then check the abort condition, then wait before the
    /// next one. `applyStep` is responsible for its own mutation AND any broadcast/UI refresh —
    /// this loop only owns sequencing and timing. Stops early (without waiting again) if
    /// `shouldAbort` returns true after a step, e.g. the fighter died or was rooted mid-path.
    public static async UniTask RunSteps<T>(IReadOnlyList<T> steps, Action<T> applyStep,
                                             int stepDurationMs, Func<bool> shouldAbort = null)
    {
        foreach (var step in steps)
        {
            applyStep(step);

            if (shouldAbort != null && shouldAbort())
                break;

            await UniTask.Delay(stepDurationMs);
        }
    }
}
