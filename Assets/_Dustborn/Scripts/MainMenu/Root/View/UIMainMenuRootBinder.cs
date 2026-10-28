using NaughtyAttributes;
using UnityEngine;
using VContainer;

public class UIMainMenuRootBinder : MonoBehaviour
{
    [SerializeField, Required] private MainScreenView _mainScreen;
    [SerializeField, Required] private NewGameScreenView _newGameScreen;
    [SerializeField, Required] private LoadGameScreenView _loadGameScreen;
    [SerializeField] private CreateWorldScreenView _createWorldScreen;
    [SerializeField, Required] private ConfirmDialogView _confirmDialog;

    [Inject]
    public void Construct(MainScreenViewModel main, NewGameScreenViewModel newGame, LoadGameScreenViewModel loadGame,
        CreateWorldScreenViewModel createWorld, ConfirmDialogViewModel dialog)
    {
        _mainScreen.Bind(main);
        _newGameScreen.Bind(newGame);
        _loadGameScreen.Bind(loadGame);

        if (_createWorldScreen != null)
            _createWorldScreen.Bind(createWorld);

        if (_confirmDialog != null)
            _confirmDialog.Bind(dialog);
    }

    private void OnDestroy()
    {
        _mainScreen.Unbind();
        _newGameScreen.Unbind();
        _loadGameScreen.Unbind();

        if (_createWorldScreen != null)
            _createWorldScreen.Unbind();

        if (_confirmDialog != null)
            _confirmDialog.Unbind();
    }
}
