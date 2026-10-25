using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Tools
{
	/// <summary>
	/// Shows the history recorded by <see cref="WiseSelectionHistoryNavigator"/> as a list of buttons.
	/// </summary>
	public class WiseSelectionHistoryWindow : EditorWindow
	{
		private Vector2 m_Scroll;
		private bool m_ScrollToEnd;
		private GUIStyle m_EntryStyle;

		[MenuItem("Edit/Selection/History Window", priority = 320)]
		static void Open() => GetWindow<WiseSelectionHistoryWindow>("Selection History");

		void OnEnable()
		{
			WiseSelectionHistoryNavigator.HistoryChanged += Repaint;
			WiseSelectionHistoryNavigator.EntryAdded += OnEntryAdded;
			EditorApplication.hierarchyChanged += Repaint; // renamed or deleted objects
		}

		void OnDisable()
		{
			WiseSelectionHistoryNavigator.HistoryChanged -= Repaint;
			WiseSelectionHistoryNavigator.EntryAdded -= OnEntryAdded;
			EditorApplication.hierarchyChanged -= Repaint;
		}

		// Only new selections scroll to the end; Back / Forward and clicks leave the scroll alone.
		void OnEntryAdded() => m_ScrollToEnd = true;

		void OnGUI()
		{
			if (m_EntryStyle == null) {
				m_EntryStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
			}

			bool vertical = position.height >= position.width;

			var prevIconSize = EditorGUIUtility.GetIconSize();
			EditorGUIUtility.SetIconSize(new Vector2(16, 16));

			// Clicks change the history, which would break the GUILayout pass if done mid-loop,
			// so they are collected here and applied once everything is drawn.
			int clickedIndex = -1;
			int navigateDirection = 0;

			if (vertical) {
				EditorGUILayout.BeginVertical();
			} else {
				EditorGUILayout.BeginHorizontal();
			}

			const float ArrowWidth = 28f;

			EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));
			using (new EditorGUI.DisabledScope(!WiseSelectionHistoryNavigator.CanGoBack)) {
				if (GUILayout.Button(EditorGUIUtility.TrIconContent("back", "Back"), GUILayout.Width(ArrowWidth))) {
					navigateDirection = -1;
				}
			}
			using (new EditorGUI.DisabledScope(!WiseSelectionHistoryNavigator.CanGoForward)) {
				if (GUILayout.Button(EditorGUIUtility.TrIconContent("forward", "Forward"), GUILayout.Width(ArrowWidth))) {
					navigateDirection = +1;
				}
			}
			EditorGUILayout.EndHorizontal();

			// The scroll view clamps this to the content size, so it lands on the last entry
			// in whichever direction the list currently runs.
			if (m_ScrollToEnd)
				m_Scroll = new Vector2(float.MaxValue, float.MaxValue);

			m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
			if (vertical) {
				EditorGUILayout.BeginVertical();
			} else {
				EditorGUILayout.BeginHorizontal();
			}

			int current = WiseSelectionHistoryNavigator.CurrentIndex;
			var prevBackground = GUI.backgroundColor;

			for (int i = 0; i < WiseSelectionHistoryNavigator.HistoryCount; i++) {
				var objects = WiseSelectionHistoryNavigator.GetHistoryEntry(i);
				var first = objects.FirstOrDefault(o => o != null);
				bool isScene = first != null && !EditorUtility.IsPersistent(first);

				if (i == current) {
					GUI.backgroundColor = Color.yellow;
				} else if (isScene) {
					GUI.backgroundColor = new Color(0.65f, 0.85f, 1f);
				}

				var options = vertical
					? new[] { GUILayout.ExpandWidth(true) }
					: new[] { GUILayout.ExpandWidth(false), GUILayout.MaxWidth(160f) };

				if (GUILayout.Button(GetContent(objects, first), m_EntryStyle, options)) {
					clickedIndex = i;
				}

				GUI.backgroundColor = prevBackground;
			}

			if (vertical) {
				EditorGUILayout.EndVertical();
			} else {
				EditorGUILayout.EndHorizontal();
			}

			EditorGUILayout.EndScrollView();

			if (vertical) {
				EditorGUILayout.EndVertical();
			} else {
				EditorGUILayout.EndHorizontal();
			}

			// The content size is only known by the Repaint pass, so keep forcing the scroll until then.
			if (Event.current.type == EventType.Repaint)
				m_ScrollToEnd = false;

			EditorGUIUtility.SetIconSize(prevIconSize);

			if (navigateDirection < 0) {
				WiseSelectionHistoryNavigator.Back();
			} else if (navigateDirection > 0) {
				WiseSelectionHistoryNavigator.Forward();
			} else if (clickedIndex >= 0) {
				WiseSelectionHistoryNavigator.SelectIndex(clickedIndex);
			}
		}

		static GUIContent GetContent(Object[] objects, Object first)
		{
			// Clicking a missing entry removes it from the history.
			if (first == null)
				return new GUIContent("(missing)", "All objects in this entry were destroyed.");

			var content = new GUIContent(EditorGUIUtility.ObjectContent(first, first.GetType()));
			content.text = objects.Length > 1 ? $"{first.name} ({objects.Length})" : first.name;
			content.tooltip = string.Join("\n", objects.Where(o => o != null).Select(o => o.name));
			return content;
		}
	}
}