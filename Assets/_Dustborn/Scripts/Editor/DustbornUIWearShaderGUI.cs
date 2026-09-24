using UnityEditor;
using UnityEngine;

namespace Dustborn.UI.Editor
{
    public sealed class DustbornUIWearShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EditorGUILayout.HelpBox("Для uGUI Image. Добавь компонент Dustborn UI Wear на тот же объект. Основной цвет задаётся в Image → Color.", MessageType.Info);
            MaterialProperty shape = FindProperty("_Shape", properties);
            EditorGUI.showMixedValue = shape.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("Форма", Mathf.RoundToInt(shape.floatValue),
                new[] { "Контур спрайта", "Горизонтальная линия", "Вертикальная линия" });
            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo("Dustborn shape");
                shape.floatValue = selected;
            }
            EditorGUI.showMixedValue = false;
            editor.ShaderProperty(FindProperty("_Color", properties), "Дополнительный Tint (обычно белый)");

            Section("Фактура поверхности");
            Show("_SurfaceStrength", "Сила потёртостей", "0 — исходная заливка; 1 — сильное локальное затемнение.");
            Show("_GrainSize", "Размер зерна", "В единицах UI. При Canvas 1920×1080 на экране 1080p одна единица равна пикселю.");
            editor.TexturePropertySingleLine(new GUIContent("Текстура (необязательно)"), FindProperty("_WearTex", properties));
            Show("_TextureInfluence", "Доля текстуры", "0 — только процедурное зерно. Текстура используется как оттенки серого.");
            Show("_TextureTileSize", "Размер повторения текстуры", "Больше значение — крупнее фактура. Импорт текстуры: sRGB Off, Wrap Repeat.");
            Show("_TextureContrast", "Контраст текстуры", "Выделяет мелкие потёртости в серой текстуре.");

            Section("Неровный край");
            Show("_EdgeRoughness", "Глубина рваности", "Сколько единиц UI можно убрать с края. Для линии толщиной 2 начинай с 0,1–0,2.");
            Show("_EdgeChipSize", "Размер неровностей", "Мелкие шероховатости или более крупные сколы. Глубина регулируется отдельно.");

            Section("Микротрещины у края");
            Show("_CrackAmount", "Количество трещин", "Вероятность трещины в одном участке. 0 полностью отключает трещины.");
            Show("_CrackWidth", "Ширина трещин", "Обычно 0,2–0,6. Широкие значения делают заметные разрывы.");
            Show("_CrackDepth", "Глубина трещин", "Насколько трещина входит внутрь от альфа-контура.");
            Show("_CrackSpacing", "Размер участка для трещин", "Больше значение — реже потенциальные трещины.");

            if (shape.hasMixedValue || selected != 0)
            {
                Section("Заострение линии");
                Show("_TaperStart", "Длина острия в начале", "Горизонтальная: слева. Вертикальная: снизу. 0 — прямой торец.");
                Show("_TaperEnd", "Длина острия в конце", "Горизонтальная: справа. Вертикальная: сверху. 0 — прямой торец.");
                Show("_TipPower", "Форма острия", "1 — прямые стороны. Больше 1 — тонкое вытянутое острие.");
                Show("_LineFeather", "Сглаживание линии", "Небольшое смягчение края; не заменяет сужение формы.");
                EditorGUILayout.HelpBox("Линия: Image Type = Simple, Source Image = None, Preserve Aspect = Off. Длину и толщину задаёт RectTransform.", MessageType.None);
            }
            Section("Вариация");
            Show("_PatternSeed", "Рисунок материала", "Сдвигает рисунок для всех объектов с этим материалом. Локальный вариант — Pattern Seed на компоненте.");

            void Show(string property, string label, string tooltip)
            {
                editor.ShaderProperty(FindProperty(property, properties), new GUIContent(label, tooltip));
            }
        }

        private static void Section(string label)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }
    }
}
