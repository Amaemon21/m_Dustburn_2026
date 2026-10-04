using KINEMATION.CharacterAnimationSystem.Examples.Scripts;
using R3;
using UnityEngine;

public class CharacterPerspectiveVisibility : MonoBehaviour
{
    [SerializeField] private SkinnedMeshRenderer[] _headSkinnedMeshRenderers;
    
    private readonly CompositeDisposable _disposable = new();

    private CharacterExampleController _characterExampleController;
    
    private void Awake()
    {
        _characterExampleController = GetComponent<CharacterExampleController>();
    }

    private void OnEnable()
    {
        /*
        _disposable.Add(_characterExampleController.IsFirstPerson.Subscribe(isFirstPerson =>
            {
                foreach (SkinnedMeshRenderer renderer in _headSkinnedMeshRenderers)
                {
                    if (renderer != null)
                        renderer.enabled = !isFirstPerson;
                }
            })
        );
        */
    }

    private void OnDestroy()
    {
        _disposable?.Dispose();
    }
}