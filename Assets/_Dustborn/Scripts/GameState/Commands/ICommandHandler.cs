public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    bool Handle(TCommand command);
}
