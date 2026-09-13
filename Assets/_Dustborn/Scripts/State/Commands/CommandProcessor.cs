using System;
using System.Collections.Generic;
using UnityEngine;

public class CommandProcessor : ICommandProcessor
{
    private readonly ISaveService _saveService;
    private readonly Dictionary<Type, object> _handlersMap = new();

    public CommandProcessor(ISaveService saveService)
    {
        _saveService = saveService;
    }

    public void RegisterHandler<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand
    {
        _handlersMap[typeof(TCommand)] = handler;
    }

    public bool Process<TCommand>(TCommand command) where TCommand : ICommand
    {
        if (!_handlersMap.TryGetValue(typeof(TCommand), out object handler))
        {
            Debug.LogError($"No handler registered for {typeof(TCommand).Name}");
            return false;
        }

        ICommandHandler<TCommand> typedHandler = (ICommandHandler<TCommand>)handler;

        if (!typedHandler.Handle(command))
            return false;

        _saveService.MarkDirty<GameStateData>();

        return true;
    }
}
