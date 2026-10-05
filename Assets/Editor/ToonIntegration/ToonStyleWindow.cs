using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wildbound.Editor
{
    public sealed class ToonStyleWindow : EditorWindow
    {
        const string LayoutPath = "Assets/Editor/ToonIntegration/ToonStyleWindow.uxml";
        SerializedObject settings;
        Label status;
        bool wasEditable = true;
        Func<ToonStyleAuthoring.Report> applyStyle;

        [MenuItem("Wildbound/Rendering/Toon Style")]
        public static void Open() => GetWindow<ToonStyleWindow>("Toon Style");

        void OnEnable() { minSize = new Vector2(360, 480); EditorApplication.update += UpdateAvailability; }
        void OnDisable() { EditorApplication.update -= UpdateAvailability; rootVisualElement.Unbind(); }

        public void CreateGUI()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ToonStyle>(ToonStyleAuthoring.ProfilePath);
            if (!profile && ToonStyleAuthoring.CanEdit) profile = ToonStyleAuthoring.LoadOrCreate();
            CreateGUIForProfile(profile, () => JsonUtility.FromJson<ToonStyleAuthoring.Report>(ToonStyleAuthoring.Apply()));
        }

        internal void CreateGUIForProfile(ToonStyle profile, Func<ToonStyleAuthoring.Report> apply)
        {
            rootVisualElement.Unbind(); rootVisualElement.Clear();
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath);
            if (!layout) { rootVisualElement.Add(new Label("Toon Style layout could not be loaded.")); return; }
            layout.CloneTree(rootVisualElement);
            status = rootVisualElement.Q<Label>("status");
            if (!profile) { status.text = "Open Toon Style in Edit mode to create its shared profile."; return; }
            settings = new SerializedObject(profile); applyStyle = apply;
            rootVisualElement.Bind(settings);
            rootVisualElement.Q<FloatField>("outlineWidth").RegisterValueChangedCallback(e =>
            {
                float width = float.IsNaN(e.newValue) || float.IsInfinity(e.newValue) ? .5f : Mathf.Clamp(e.newValue, 0, 10);
                if (width != e.newValue) rootVisualElement.Q<FloatField>("outlineWidth").value = width;
            });
            var shadows = rootVisualElement.Q<VisualElement>("shadowControls");
            shadows.SetEnabled(profile.overrideToonShadows);
            rootVisualElement.Q<Toggle>("overrideToonShadows").RegisterValueChangedCallback(e => shadows.SetEnabled(e.newValue));
            var reception = rootVisualElement.Q<Toggle>("receiveProjectedShadows");
            reception.SetEnabled(profile.overrideShadowReception);
            rootVisualElement.Q<Toggle>("overrideShadowReception").RegisterValueChangedCallback(e => reception.SetEnabled(e.newValue));
            foreach (string name in new[] { "firstShadeBrightness", "secondShadeBrightness", "firstShadeThreshold", "secondShadeThreshold", "firstShadeFeather", "secondShadeFeather" })
                rootVisualElement.Q<Slider>(name).RegisterValueChangedCallback(e =>
                {
                    settings.ApplyModifiedProperties(); profile.Normalize(); settings.Update();
                });
            rootVisualElement.Q<Button>("apply").clicked += Apply;
            status.text = "Edit the profile, then apply it. New project-generated materials use this profile automatically.";
            UpdateAvailability();
        }

        void Apply()
        {
            try
            {
                ToonStyleAuthoring.RequireEditMode();
                settings.ApplyModifiedProperties();
                var report = applyStyle();
                status.text = $"Applied to {report.materials} Toon materials ({report.changed} changed). No scenes saved.";
            }
            catch (Exception exception) { status.text = exception.Message; }
        }

        void UpdateAvailability()
        {
            if (status == null) return;
            bool editable = ToonStyleAuthoring.CanEdit;
            rootVisualElement.Q<VisualElement>("settings")?.SetEnabled(editable);
            rootVisualElement.Q<Button>("apply")?.SetEnabled(editable && settings != null);
            if (!editable) status.text = "Changes are disabled during Play mode, compilation or import.";
            else if (!wasEditable) status.text = settings == null ? "Reopen Toon Style in Edit mode to load its shared profile." :
                "Edit the profile, then apply it. New project-generated materials use this profile automatically.";
            wasEditable = editable;
        }
    }
}
