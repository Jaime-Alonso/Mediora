# Mediora Wiki

Welcome to the Mediora documentation hub.

Mediora is a mediator library for .NET inspired by the MediatR programming model, with a strong focus on predictable behavior, clear defaults, and production reliability.

## Start here

- [Getting Started](Getting-Started.md)
- [DI Registration](DI-Registration.md)
- [Pipeline Behaviors](Pipeline-Behaviors.md)
- [Notification Publish Modes](Notification-Publish-Modes.md)
- [Caching](Caching.md)

## At a glance

- **Contracts package** (`Mediora.Contracts`): request/notification/stream contracts and mediator abstractions.
- **Runtime package** (`Mediora`): dispatching, runtime wrappers, and `AddMediora(...)` registration.
- **Explicit behavior registration**: request and stream behaviors are registered intentionally in `AddMediora(...)`.
- **Notification publish strategies**: choose per notification type between fail-fast sequential, aggregate sequential, or parallel aggregate.

## Who should read what

- If you are integrating Mediora for the first time, start with [Getting Started](Getting-Started.md).
- If you are wiring DI and handler scanning, read [DI Registration](DI-Registration.md).
- If you are adding cross-cutting concerns, read [Pipeline Behaviors](Pipeline-Behaviors.md).
- If you publish domain events, read [Notification Publish Modes](Notification-Publish-Modes.md).
- If you care about runtime throughput and reflection costs, read [Caching](Caching.md).

## Related

- [Repository README](../README.md)
