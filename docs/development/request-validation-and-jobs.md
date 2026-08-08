# Request validation and job scheduling

Request validators own syntactic/structural input validity. Job schedulers own the
translation from one scheduling request to Quartz trigger/data-map configuration.
Neither should absorb business workflow logic.

## Request validation

FluentValidation validators live beside their request type and are injected through
`IValidator<TRequest>`. Needlr discovers them automatically.

Use explicit messages when a default message would not tell a caller how to correct
the input. Cross-field conditions belong in one validator through `When`/`Must` rather
than being duplicated in handlers or unit-of-work code.

Endpoint handlers call `ValidateAsync`, propagate cancellation, and convert invalid
results to the HTTP result contract. Exceptions are not validation control flow.

Business invariants can still be enforced by the operation/result pattern after
structural validation succeeds.

## Job schedulers

Each scheduled job has a dedicated scheduler interface and implementation in the same
vertical slice.

The scheduler builds a `JobDataMap`, constructs the trigger, and delegates to the
shared one-shot scheduler. It does not call repositories, invoke unit-of-work business
logic, or execute the job itself.

Data-map keys are constants owned by the job so scheduler and executor cannot drift.
Callers depend on the scheduler interface; Needlr discovers the implementation.

Scheduling logs use generated methods and include stable job/business identifiers
without logging payloads.
