using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;
using VContainer;

internal static class InteractionHarness
{
    private static int _checks;

    private static async Task Main()
    {
        using WindowService windows = new();
        foreach (string id in new[] { "inventory", "map", "container", "before-service" })
            windows.Register(id, new TestWindowPresenter());
        ScreenService screen = new(windows);
        using InteractedViewModel viewModel = new();
        using PlayerInputService input = new();
        TestTargets targets = new();
        InteractHandler handler = new(targets, screen, viewModel, new TestContext(), input);
        using InteractController controller = new(handler, input, screen);

        TestObject first = new("Open chest");
        TestObject second = new("Pick up stone");
        targets.Target = first;
        controller.PostLateTick();
        Check(first.ShowCount == 0, "no detection before gameplay starts");
        controller.Activate();
        controller.Activate();
        Check(input.EnableCount == 0, "interaction controller does not enable shared input");
        input.Enable();
        controller.PostLateTick();
        controller.PostLateTick();
        Check(first.ShowCount == 1, "outline applied only on target change");
        Check(viewModel.ContextActionText.CurrentValue == $"[E] {first.InteractKey}" && viewModel.ContextActionAlpha.CurrentValue == 1f,
            "target prompt displays interaction binding");
        input.InteractBindingDisplayString = "F";
        controller.PostLateTick();
        Check(viewModel.ContextActionText.CurrentValue == $"[F] {first.InteractKey}" && first.ShowCount == 1,
            "binding change updates prompt without changing target");
        input.InteractBindingDisplayString = string.Empty;
        controller.PostLateTick();
        Check(viewModel.ContextActionText.CurrentValue == first.InteractKey, "unbound action leaves no empty key brackets");
        input.InteractBindingDisplayString = "E";

        targets.Target = second;
        input.Press();
        Check(first.InteractCount == 0 && second.InteractCount == 1, "input rescans current aim instead of using stale target");
        Check(first.HideCount == 1 && second.ShowCount == 1, "target switch updates outlines once");

        screen.Open("inventory", new TestWindow());
        Check(second.HideCount == 1 && viewModel.ContextActionAlpha.CurrentValue == 0f, "opening window immediately clears target");
        input.Press();
        controller.PostLateTick();
        Check(second.InteractCount == 1 && second.ShowCount == 1, "window blocks input and detection");
        screen.Open("map", new TestWindow());
        screen.Close("inventory");
        Check(screen.HasAnyWindowOpen() && screen.HasOpenWindows.CurrentValue, "other open window keeps gameplay blocked");
        screen.Close("map");
        controller.PostLateTick();
        Check(second.ShowCount == 2, "closing last window resumes detection");

        second.CanInteract = false;
        input.Press();
        Check(second.InteractCount == 1 && viewModel.ContextActionText.CurrentValue == string.Empty, "unavailable object cannot be used");
        second.CanInteract = true;
        second.OnInteract = () => screen.Open("container", new TestWindow());
        input.Press();
        Check(screen.HasAnyWindowOpen() && viewModel.ContextActionAlpha.CurrentValue == 0f, "interaction opening window clears prompt");
        screen.CloseAll();

        second.OnInteract = () => targets.Target = null;
        input.Press();
        Check(viewModel.ContextActionAlpha.CurrentValue == 0f, "interaction removing target clears prompt");
        targets.Target = first;
        controller.PostLateTick();
        controller.Stop();
        int count = first.InteractCount;
        input.Press();
        controller.PostLateTick();
        Check(first.InteractCount == count && viewModel.ContextActionAlpha.CurrentValue == 0f && input.Enabled, "stop releases subscriptions and leaves shared input enabled");
        controller.Activate();
        input.Press();
        Check(first.InteractCount == count + 1, "restart has one input subscription");
        controller.Dispose();
        count = first.InteractCount;
        input.Press();
        Check(first.InteractCount == count, "disposal removes input subscription");
        Check(input.Enabled && input.EnableCount == 1, "interaction lifecycle never changes shared input ownership");

        screen.Open("before-service", new TestWindow());
        ScreenService lateScreen = new(windows);
        Check(lateScreen.HasOpenWindows.CurrentValue, "screen initializes from existing windows");
        lateScreen.CloseAll();
        Check(!screen.HasOpenWindows.CurrentValue && !lateScreen.HasOpenWindows.CurrentValue, "screen adapters observe same window state");

        ContainerBuilder builder = new();
        builder.Register<FirstActivatable>(Lifetime.Singleton).AsSelf().As<IGameplayActivatable>();
        builder.Register<SecondActivatable>(Lifetime.Singleton).AsSelf().As<IGameplayActivatable>();
        using IObjectResolver container = builder.Build();
        FirstActivatable firstActivatable = container.Resolve<FirstActivatable>();
        SecondActivatable secondActivatable = container.Resolve<SecondActivatable>();
        Check(firstActivatable.ActivationCount == 0 && secondActivatable.ActivationCount == 0,
            "building the scene container activates nothing");
        IReadOnlyList<IGameplayActivatable> activatables = container.Resolve<IReadOnlyList<IGameplayActivatable>>();
        Check(activatables.Count == 2 && ReferenceEquals(activatables[0], firstActivatable) && ReferenceEquals(activatables[1], secondActivatable),
            "DI resolves the activation list in registration order without duplicate instances");
        foreach (IGameplayActivatable activatable in activatables)
            activatable.Activate();
        Check(firstActivatable.ActivationCount == 1 && secondActivatable.ActivationCount == 1, "every service is activated once");

        TestContext pickupContext = new() { Capacity = 2 };
        ItemPickup pickup = new("stone", 5);
        pickup.PickUp(pickupContext);
        Check(pickup.Remaining == 3 && pickupContext.Received == 2 && pickupContext.ItemId == "stone", "partial pickup keeps remaining stack in world");
        pickup.PickUp(pickupContext);
        Check(pickup.Remaining == 3, "full inventory does not consume world item");
        pickupContext.Capacity = 8;
        pickup.PickUp(pickupContext);
        Check(pickup.Remaining == 0 && pickupContext.Received == 5, "all remaining items can be picked up later");
        pickup.PickUp(pickupContext);
        Check(pickupContext.Received == 5, "consumed pickup cannot add items twice");

        await VerifyPlayerControl();
        Console.WriteLine($"PASS: {_checks} interaction lifecycle and window checks");
    }

    private static async Task VerifyPlayerControl()
    {
        using WindowService windows = new();
        windows.Register("menu", new TestWindowPresenter());
        windows.Register("other", new TestWindowPresenter());
        ScreenService screen = new(windows);
        PlayerSpawnService player = new();
        PlayerInputService input = new();
        using PlayerControlService control = new(player, screen, input);
        Check(!input.Enabled && !player.ControlEnabled, "player input remains disabled before gameplay run");
        control.Activate();
        Check(input.Enabled && player.ControlEnabled, "player control enables gameplay input");
        windows.Open("menu", new TestWindow());
        Check(!input.Enabled && !player.ControlEnabled, "open window disables player input and movement");
        windows.Open("other", new TestWindow());
        windows.Close("menu");
        Check(!input.Enabled && !player.ControlEnabled, "remaining window keeps gameplay input disabled");
        windows.Close("other");
        Check(input.Enabled && player.ControlEnabled, "closing last window restores gameplay input");
        control.Dispose();
        Check(!input.Enabled && !player.ControlEnabled, "player control disposal disables gameplay input");
        windows.Open("menu", new TestWindow());
        windows.Close("menu");
        Check(!input.Enabled, "disposed player control no longer reacts to window changes");
        using PlayerControlService blocked = new(player, screen, input);
        windows.Open("menu", new TestWindow());
        blocked.Activate();
        Check(!input.Enabled && !player.ControlEnabled, "initial open window prevents enabling gameplay input");
    }

    private static void Check(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
        _checks++;
    }

    private sealed class TestWindow : WindowViewModel { }

    private sealed class FirstActivatable : IGameplayActivatable
    {
        public int ActivationCount { get; private set; }
        public void Activate() => ActivationCount++;
    }

    private sealed class SecondActivatable : IGameplayActivatable
    {
        public int ActivationCount { get; private set; }
        public void Activate() => ActivationCount++;
    }

    private sealed class TestTargets : IInteractionTargetProvider
    {
        public IInteractableObject Target { get; set; }
        public IInteractableObject FindTarget() => Target;
    }

    private sealed class TestContext : IInteractionContext
    {
        public int Capacity { get; set; }
        public int Received { get; private set; }
        public string ItemId { get; private set; }
        public int PickUp(string itemId, int amount)
        {
            ItemId = itemId;
            int accepted = Math.Min(Capacity, amount);
            Capacity -= accepted;
            Received += accepted;
            return accepted;
        }
    }

    private sealed class TestObject : IInteractableObject
    {
        public string InteractKey { get; }
        public bool CanInteract { get; set; } = true;
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public int InteractCount { get; private set; }
        public Action OnInteract { get; set; }
        public TestObject(string key) => InteractKey = key;
        public void ShowOutline() => ShowCount++;
        public void HideOutline() => HideCount++;
        public bool IsInteractable() => CanInteract;
        public void Interact(IInteractionContext context) { InteractCount++; OnInteract?.Invoke(); }
    }
}
