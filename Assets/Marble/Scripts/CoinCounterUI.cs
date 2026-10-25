using TMPro;
using UnityEngine;

/// <summary>
/// Writes the coin count into a TextMeshPro label. Put it on any object in your canvas.
/// </summary>
public class CoinCounterUI : MonoBehaviour
{
    public TMP_Text label;

    [Tooltip("{0} = collected coins, {1} = total coins, {2} = score (sum of coin values).")]
    public string format = "{0} / {1}";

    [Tooltip("Optional object (e.g. a 'All coins collected!' panel) shown once every coin is picked up.")]
    public GameObject allCollectedPanel;

    void Reset()
    {
        label = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        CoinCounter.Changed += Refresh;
        CoinCounter.AllCollected += ShowAllCollected;
        if (allCollectedPanel != null)
            allCollectedPanel.SetActive(false);
        Refresh();
    }

    void OnDisable()
    {
        CoinCounter.Changed -= Refresh;
        CoinCounter.AllCollected -= ShowAllCollected;
    }

    void Refresh()
    {
        if (label != null)
            label.text = string.Format(format, CoinCounter.CollectedCoins, CoinCounter.TotalCoins, CoinCounter.Score);
    }

    void ShowAllCollected()
    {
        if (allCollectedPanel != null)
            allCollectedPanel.SetActive(true);
    }
}
