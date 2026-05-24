using UnityEngine;
using TMPro;

public class SliderValueDisplay : MonoBehaviour
{
    // 將 New Text 物件拖到這個格子裡
    public TextMeshProUGUI targetText; 

    // 這個函數會出現在 Slider 的選單中
    public void UpdateText(float value)
    {
        if (targetText != null)
        {
            // 顯示整數
            targetText.text = Mathf.RoundToInt(value).ToString();
        }
    }
}