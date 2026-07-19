using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// One-click generator for the shared humanoid Animator Controller every
/// runtime-spawned player uses. Reads Vigo's six FBX clips as the canonical
/// motion set (Meshy's Fixed rig means the same clips retarget cleanly onto
/// Mira / Kai / Nova / Sunny at runtime), forces sensible loop-time flags on
/// each clip's import settings, then builds:
///
///   • Parameters: Speed (float), Jump / Hit / Punch (triggers)
///   • Locomotion state — 1D BlendTree on Speed: Idle @ 0 → Walk @ 1 → Run @ 2
///   • Any State → Jump (fires on Jump trigger, returns to Locomotion on 90 % of clip)
///   • Any State → Hit  (fires on Hit trigger, same return)
///   • Any State → Punch (fires on Punch trigger, same return)
///
/// Saves to Assets/_Animations/PlayerAnimator.controller. Idempotent — running
/// twice replaces the existing controller rather than accumulating states.
///
/// Invoke from Tools/Chase Us/Build Player Animator Controller.
/// </summary>
public static class BuildPlayerAnimatorController
{
    private const string OutputDir = "Assets/_Animations";
    private const string OutputPath = OutputDir + "/PlayerAnimator.controller";
    private const string ClipSourceFolder = "Assets/_Import/Meshy/Character/Vigo";

    // FBX file → clip role in the controller. The file names come from the
    // rename pass on 2026-07-19; if you rename the roster again, update these
    // in lockstep.
    private static readonly (string fbxName, string role, bool loop, string clipRename)[] Sources =
    {
        ("Vigo_Base.fbx",  "Idle",  true,  "Idle"),
        ("Vigo.fbx",       "Walk",  true,  "Walk"),
        ("Vigo_Run.fbx",   "Run",   true,  "Run"),
        ("Vigo_Jump.fbx",  "Jump",  false, "Jump"),
        ("Vigo_Hit.fbx",   "Hit",   false, "Hit"),
        ("Vigo_Punch.fbx", "Punch", false, "Punch"),
    };

    [MenuItem("Tools/Chase Us/Build Player Animator Controller")]
    public static void Build()
    {
        if (!System.IO.Directory.Exists(OutputDir))
        {
            System.IO.Directory.CreateDirectory(OutputDir);
            AssetDatabase.Refresh();
        }

        // Step 1 — normalize import settings on each source FBX so the clip
        // loops correctly and has a stable, human-readable name.
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var src in Sources)
        {
            string path = $"{ClipSourceFolder}/{src.fbxName}";
            AnimationClip clip = NormalizeAndLoadClip(path, src.clipRename, src.loop);
            if (clip == null)
            {
                Debug.LogError($"[BuildPlayerAnimatorController] Aborting — could not resolve a clip in {path}. " +
                               $"Make sure the FBX is imported with Rig = Humanoid and contains at least one animation take.");
                return;
            }
            clips[src.role] = clip;
        }

        // Step 2 — build (or replace) the controller. Deleting first is
        // cleaner than trying to reconcile stale states from a prior run.
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(OutputPath) != null)
        {
            AssetDatabase.DeleteAsset(OutputPath);
        }
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(OutputPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Jump",  AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit",   AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Punch", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // Step 3 — Locomotion blend tree. `CreateBlendTreeInController` returns
        // the tree AND creates a state hosting it in one shot.
        AnimatorState locomotion = sm.AddState("Locomotion");
        locomotion.motion = BuildLocomotionTree(controller, clips["Idle"], clips["Walk"], clips["Run"]);
        sm.defaultState = locomotion;

        // Step 4 — one-shot states for Jump / Hit / Punch, each transitioned
        // into by Any State on their trigger and back out on 90 % of the clip.
        AddOneShotState(sm, controller, locomotion, "Jump",  clips["Jump"],  "Jump");
        AddOneShotState(sm, controller, locomotion, "Hit",   clips["Hit"],   "Hit");
        AddOneShotState(sm, controller, locomotion, "Punch", clips["Punch"], "Punch");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[BuildPlayerAnimatorController] Wrote {OutputPath} with 4 parameters, locomotion blend tree, and 3 one-shot states. " +
                  $"Drop it into PlayerCharacterVisual → Player Controller field on the Player_Network prefab.");
        EditorGUIUtility.PingObject(controller);
        Selection.activeObject = controller;
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Ensures the first take on the FBX is renamed to <paramref name="clipName"/>
    /// and its Loop Time flag matches <paramref name="loop"/>. Returns the loaded
    /// clip after the reimport.
    /// </summary>
    private static AnimationClip NormalizeAndLoadClip(string fbxPath, string clipName, bool loop)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[BuildPlayerAnimatorController] No ModelImporter at {fbxPath} — was the file renamed or deleted?");
            return null;
        }

        // Force Humanoid — retargeting the shared clips onto other roster
        // members' avatars requires it. No-op if the user already applied it.
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
        }

        ModelImporterClipAnimation[] clipAnims = importer.defaultClipAnimations;
        if (clipAnims == null || clipAnims.Length == 0)
        {
            Debug.LogError($"[BuildPlayerAnimatorController] {fbxPath} has no animation takes. Reimport with Rig = Humanoid and confirm the FBX actually contains an animation clip.");
            return null;
        }

        // Only modify the first take — Meshy exports one clip per FBX.
        clipAnims[0].name = clipName;
        clipAnims[0].loopTime = loop;
        clipAnims[0].loopPose = loop;
        importer.clipAnimations = clipAnims;
        importer.SaveAndReimport();

        // After reimport the AnimationClip sub-asset name matches clipAnims[0].name.
        foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (obj is AnimationClip clip && clip.name == clipName)
            {
                return clip;
            }
        }

        Debug.LogError($"[BuildPlayerAnimatorController] Could not find a clip named '{clipName}' in {fbxPath} after reimport.");
        return null;
    }

    private static BlendTree BuildLocomotionTree(AnimatorController controller, AnimationClip idle, AnimationClip walk, AnimationClip run)
    {
        BlendTree tree = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false,
            minThreshold = 0f,
            maxThreshold = 2f,
        };
        // BlendTree must be a sub-asset of the controller for Unity to serialize
        // it — CreateBlendTreeInController does this automatically, but since we
        // built the tree by hand we register it ourselves.
        AssetDatabase.AddObjectToAsset(tree, controller);

        tree.AddChild(idle, 0f);
        tree.AddChild(walk, 1f);
        tree.AddChild(run,  2f);
        return tree;
    }

    private static void AddOneShotState(AnimatorStateMachine sm,
                                        AnimatorController controller,
                                        AnimatorState returnTo,
                                        string stateName,
                                        AnimationClip clip,
                                        string trigger)
    {
        AnimatorState state = sm.AddState(stateName);
        state.motion = clip;

        // Any State → this state on the trigger. hasExitTime false so it fires
        // as soon as the trigger is set, mid-clip if we're already blending.
        AnimatorStateTransition inTransition = sm.AddAnyStateTransition(state);
        inTransition.hasExitTime = false;
        inTransition.duration = 0.08f;   // small crossfade to avoid pops
        inTransition.canTransitionToSelf = false;
        inTransition.AddCondition(AnimatorConditionMode.If, 0f, trigger);

        // Back to Locomotion at ~90 % of the one-shot clip so the tail blends
        // out cleanly into the next locomotion state.
        AnimatorStateTransition outTransition = state.AddTransition(returnTo);
        outTransition.hasExitTime = true;
        outTransition.exitTime = 0.9f;
        outTransition.duration = 0.15f;
    }
}
