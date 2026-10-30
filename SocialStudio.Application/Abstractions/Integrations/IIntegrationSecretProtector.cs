namespace SocialStudio.Application.Abstractions.Integrations;

public interface IIntegrationSecretProtector
{
    string Protect(string value);

    string Unprotect(string protectedValue);
}