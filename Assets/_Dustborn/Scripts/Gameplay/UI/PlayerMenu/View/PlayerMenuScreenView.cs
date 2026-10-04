using System;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerMenuScreenView : WindowView
{
    [Serializable]
    private sealed class TabBinding
    {
        public PlayerMenuTab Tab;
        public Button Button;
        public TMP_Text Text;
        public GameObject Content;
        public GameObject Selection;
    }

    [SerializeField] private TabBinding[] _tabs = Array.Empty<TabBinding>();
    [SerializeField] private InventoryTabView _inventoryTab;

    [SerializeField] private Color _selectionColor = Color.white;
    [SerializeField] private Color _unselectionColor = Color.black;
    
    public override string WindowId => PlayerMenuService.PLAYER_MENU_WINDOW;
    public override Type ViewModelType => typeof(PlayerMenuScreenViewModel);

    protected override void BindCore(WindowViewModel viewModel, CompositeDisposable bindings)
    {
        PlayerMenuScreenViewModel menu = (PlayerMenuScreenViewModel)viewModel;
        
        _inventoryTab.Bind(menu.Inventory);
        
        foreach (TabBinding tab in _tabs)
        {
            bindings.Add(tab.Button.OnClickAsObservable().Subscribe(_ => menu.SelectTab.Execute(tab.Tab)));
            
            bindings.Add(menu.SelectedTab.Subscribe(selected =>
            {
                bool active = selected == tab.Tab;
                tab.Content.SetActive(active);
                tab.Selection.SetActive(active);
                tab.Text.color = active ? _selectionColor : _unselectionColor;
            }));
        }
    }

    protected override void OnUnbound()
    {
        _inventoryTab.Unbind();
    }
}