using System;
using System.Linq;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using UnityWeld.Binding;
using UnityWeld.Binding.Internal;

namespace UnityWeld_Editor
{
    [CustomEditor(typeof(AnimatorParameterBinding))]
    public class AnimatorParameterBindingEditor : BaseBindingEditor
    {
        private AnimatorParameterBinding targetScript;

        private AnimBool viewAdapterOptionsFade;

        // Whether each property in the target differs from the prefab it uses.
        private bool viewAdapterPrefabModified;
        private bool viewAdapterOptionsPrefabModified;
        private bool viewModelPropertyPrefabModified;
        private bool viewPropertyPrefabModified;

        private void OnEnable()
        {
            // Initialise reference to target script
            targetScript = (AnimatorParameterBinding)target;

            Type adapterType;

            viewAdapterOptionsFade = new AnimBool(
                ShouldShowAdapterOptions(targetScript.ViewAdapterTypeName, out adapterType)
            );

            viewAdapterOptionsFade.valueChanged.AddListener(Repaint);
        }

        private void OnDisable()
        {
            viewAdapterOptionsFade.valueChanged.RemoveListener(Repaint);
        }

        public override void OnInspectorGUI()
        {
            // Because of some of the work arounds done due to Unity's undocumented behaviour,
            // When testing in the editor, some of this may run and can produce unexpected results
            // So we disable this all while in play mode
            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Cannot modify in play mode!", MessageType.Warning);
                return;
            }

            UpdatePrefabModifiedProperties();

            var defaultLabelStyle = EditorStyles.label.fontStyle;
            EditorStyles.label.fontStyle = viewPropertyPrefabModified ? FontStyle.Bold : defaultLabelStyle;

            Type viewPropertyType;
            ShowAnimatorParametersMenu(
                new GUIContent("View property", "Property on the view to bind to"),
                updatedValue =>
                {
                    targetScript.AnimatorParameterName = updatedValue.Name;
                    targetScript.AnimatorParameterType = updatedValue.Type;
                },
                new AnimatorParameterTypeAndName(targetScript.AnimatorParameterName, targetScript.AnimatorParameterType),
                out viewPropertyType
                );

            // Don't let the user set anything else until they've chosen a view property.
            var guiPreviouslyEnabled = GUI.enabled;
            if (string.IsNullOrEmpty(targetScript.AnimatorParameterName))
            {
                GUI.enabled = false;
            }

            var viewAdapterTypeNames = GetAdapterTypeNames(
                type => viewPropertyType == null ||
                    TypeResolver.FindAdapterAttribute(type).OutputType == viewPropertyType
            );

            EditorStyles.label.fontStyle = viewAdapterPrefabModified ? FontStyle.Bold : defaultLabelStyle;

            ShowAdapterMenu(
                new GUIContent("View adapter", "Adapter that converts values sent from the view-model to the view."),
                viewAdapterTypeNames,
                targetScript.ViewAdapterTypeName,
                newValue =>
                {
                    // Get rid of old adapter options if we changed the type of the adapter.
                    if (newValue != targetScript.ViewAdapterTypeName)
                    {
                        Undo.RecordObject(targetScript, "Set view adapter options");
                        targetScript.ViewAdapterOptions = null;
                    }

                    UpdateProperty(
                        updatedValue => targetScript.ViewAdapterTypeName = updatedValue,
                        targetScript.ViewAdapterTypeName,
                        newValue,
                        "Set view adapter"
                    );
                }
            );

            Type adapterType;
            viewAdapterOptionsFade.target = ShouldShowAdapterOptions(targetScript.ViewAdapterTypeName, out adapterType);

            EditorStyles.label.fontStyle = viewAdapterOptionsPrefabModified ? FontStyle.Bold : defaultLabelStyle;

            ShowAdapterOptionsMenu(
                "View adapter options",
                adapterType,
                options => targetScript.ViewAdapterOptions = options,
                targetScript.ViewAdapterOptions,
                viewAdapterOptionsFade.faded
            );

            EditorGUILayout.Space();

            EditorStyles.label.fontStyle = viewModelPropertyPrefabModified ? FontStyle.Bold : defaultLabelStyle;

            var adaptedViewPropertyType = AdaptTypeBackward(viewPropertyType, targetScript.ViewAdapterTypeName);
            ShowViewModelPropertyMenu(
                new GUIContent("View-model property", "Property on the view-model to bind to."),
                TypeResolver.FindBindableProperties(targetScript),
                updatedValue => targetScript.ViewModelPropertyName = updatedValue,
                targetScript.ViewModelPropertyName,
                property => property.PropertyType == adaptedViewPropertyType
            );

            GUI.enabled = guiPreviouslyEnabled;

            EditorStyles.label.fontStyle = defaultLabelStyle;

            EditorGUILayout.Space();

        }

        private void ShowAnimatorParametersMenu(
            GUIContent label,
            Action<AnimatorParameterTypeAndName> propertyValueSetter,
            AnimatorParameterTypeAndName curPropertyValue,
            out Type selectedPropertyType
        )
        {
            var animator = targetScript.GetComponent<Animator>();

            AnimatorControllerParameter[] properties = new AnimatorControllerParameter[] { };

            // For whatever reason, accessing Animator.parameters while the GameObject the
            // Animator is on is inactive causes it to return an empty array, aswell as fill
            // the unity console with warnings. This behaviour is undocumented, and this is just
            // a work around which seems to work fine
            if (!animator.gameObject.activeInHierarchy)
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(animator.transform);
                var parent = animator.transform.parent;
                var siblingIndex = animator.transform.GetSiblingIndex();
                var isActive = animator.gameObject.activeSelf;
                animator.transform.SetParent(null);
                animator.gameObject.SetActive(true);
                properties = animator.parameters;
                animator.transform.SetParent(parent);
                animator.transform.SetSiblingIndex(siblingIndex);
                animator.gameObject.SetActive(isActive);
                UnityEditorInternal.ComponentUtility.PasteComponentValues(animator.transform);
            }
            else
            {
                properties = animator.parameters;
                //Another odd fix to refresh the parameters
                if (properties == null || properties.Length == 0)
                {
                    var isActive = animator.gameObject.activeSelf;
                    animator.gameObject.SetActive(false);
                    animator.gameObject.SetActive(true);
                    animator.gameObject.SetActive(isActive);
                    properties = animator.parameters;
                }
            }

            var propertyNamesAndTypes = properties
                .Select(m => new AnimatorParameterTypeAndName(m.name, m.type))
                .ToArray();
            var selectedIndex = Array.IndexOf(propertyNamesAndTypes, curPropertyValue);
            var content = properties.Select(prop => new GUIContent(string.Concat(
                    prop.name,
                    " : ",
                    prop.type.ToString()
                )))
                .ToArray();

            var newSelectedIndex = EditorGUILayout.Popup(label, selectedIndex, content);
            if (newSelectedIndex != selectedIndex)
            {
                var newSelectedProperty = properties[newSelectedIndex];

                UpdateProperty(
                    propertyValueSetter,
                    curPropertyValue,
                    new AnimatorParameterTypeAndName(newSelectedProperty.name, newSelectedProperty.type),
                    "Set Animator parameter"
                );

                switch (newSelectedProperty.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        {
                            selectedPropertyType = typeof(bool);
                            break;
                        }
                    case AnimatorControllerParameterType.Float:
                        {
                            selectedPropertyType = typeof(float);
                            break;
                        }
                    case AnimatorControllerParameterType.Int:
                        {
                            selectedPropertyType = typeof(int);
                            break;
                        }
                    case AnimatorControllerParameterType.Trigger:
                        {
                            selectedPropertyType = typeof(AnimatorParameterTrigger);
                            break;
                        }
                    default:
                        {
                            selectedPropertyType = null;
                            break;
                        }
                }
            }
            else
            {
                if (selectedIndex < 0)
                {
                    selectedPropertyType = null;
                    return;
                }

                switch (properties[selectedIndex].type)
                {
                    case AnimatorControllerParameterType.Bool:
                        {
                            selectedPropertyType = typeof(bool);
                            break;
                        }
                    case AnimatorControllerParameterType.Float:
                        {
                            selectedPropertyType = typeof(float);
                            break;
                        }
                    case AnimatorControllerParameterType.Int:
                        {
                            selectedPropertyType = typeof(int);
                            break;
                        }
                    case AnimatorControllerParameterType.Trigger:
                        {
                            selectedPropertyType = typeof(AnimatorParameterTrigger);
                            break;
                        }
                    default:
                        {
                            selectedPropertyType = null;
                            break;
                        }
                }
            }
        }

        /// <summary>
        /// Check whether each of the properties on the object have been changed from the value in the prefab.
        /// </summary>
        private void UpdatePrefabModifiedProperties()
        {
            var property = serializedObject.GetIterator();
            // Need to call Next(true) to get the first child. Once we have it, Next(false)
            // will iterate through the properties.
            property.Next(true);
            do
            {
                switch (property.name)
                {
                    case "viewAdapterTypeName":
                        viewAdapterPrefabModified = property.prefabOverride;
                        break;

                    case "viewAdapterOptions":
                        viewAdapterOptionsPrefabModified = property.prefabOverride;
                        break;

                    case "viewModelPropertyName":
                        viewModelPropertyPrefabModified = property.prefabOverride;
                        break;

                    case "uiPropertyName":
                        viewPropertyPrefabModified = property.prefabOverride;
                        break;

                    default:
                        break;
                }
            }
            while (property.Next(false));
        }

        private class AnimatorParameterTypeAndName
        {
            public string Name { get; private set; }
            public AnimatorControllerParameterType Type { get; private set; }
            public AnimatorParameterTypeAndName(string name, AnimatorControllerParameterType type)
            {
                Name = name;
                Type = type;
            }

            public override bool Equals(object obj)
            {
                if (obj is AnimatorParameterTypeAndName)
                {
                    var objAsThis = obj as AnimatorParameterTypeAndName;
                    return objAsThis.Name == Name && objAsThis.Type == Type;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return new { Name = Name, Type = Type }.GetHashCode();
            }
        }
    }
}
