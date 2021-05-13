﻿using JetBrains.Annotations;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Robust.Server.Console.Commands
{
    [UsedImplicitly]
    internal sealed class RemoveComponentCommand : IConsoleCommand
    {
        public string Command => "rmcomp";
        public string Description => "Removes a component from an entity.";
        public string Help => $"{Command} <uid> <componentName>";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length != 2)
            {
                shell.WriteLine($"Invalid amount of arguments.\n{Help}.");
                return;
            }

            if (!EntityUid.TryParse(args[0], out var uid))
            {
                shell.WriteLine($"{uid} is not a valid entity uid.");
                return;
            }

            var entityManager = IoCManager.Resolve<IEntityManager>();

            if (!entityManager.TryGetEntity(uid, out var entity))
            {
                shell.WriteLine($"No entity found with id {uid}.");
                return;
            }

            var componentName = args[1];

            var compManager = IoCManager.Resolve<IComponentManager>();
            var compFactory = IoCManager.Resolve<IComponentFactory>();

            if (!compFactory.TryGetRegistration(componentName, out var registration, true))
            {
                shell.WriteLine($"No component found with name {componentName}.");
                return;
            }

            if (!entity.HasComponent(registration.Type))
            {
                shell.WriteLine($"No {componentName} component found on entity {entity.Name}.");
                return;
            }

            compManager.RemoveComponent(uid, registration.Type);

            shell.WriteLine($"Removed {componentName} component from entity {entity.Name}.");
        }
    }
}
