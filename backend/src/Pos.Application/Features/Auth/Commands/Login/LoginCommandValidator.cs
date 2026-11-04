using FluentValidation;

namespace Pos.Application.Features.Auth.Commands.Login;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.Username)
            .NotEmpty().WithErrorCode("AUTH.USERNAME_REQUIRED").WithMessage("Ingrese su usuario.")
            .Must(BeShortEnough).WithErrorCode("AUTH.FIELD_TOO_LONG").WithMessage("El usuario es demasiado largo.");

        RuleFor(c => c.Password)
            .NotEmpty().WithErrorCode("AUTH.PASSWORD_REQUIRED").WithMessage("Ingrese su contraseña.")
            .Must(BeShortEnough).WithErrorCode("AUTH.FIELD_TOO_LONG").WithMessage("La contraseña es demasiado larga.");
    }

    private static bool BeShortEnough(string? value) => value is null || value.Length <= LoginCommand.MaxFieldLength;
}
