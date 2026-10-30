using System.Reflection;
using FluentValidation;
using SocialStudio.Application.Abstractions.ContentAnalysis;
using SocialStudio.Application.Common;
using SocialStudio.Application.Features.ContentAnalysis;
using SocialStudio.Application.Features.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentStudio.Operations;
using SocialStudio.Application.Features.Social.SingleAnalyzer.Analyze;
using SocialStudio.Application.Features.Social.SingleAnalyzer.Profile;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Application;

/// <summary>
/// Composition root for the Application layer.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers MediatR handlers, FluentValidation validators, and cross-cutting pipeline behaviors.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddScoped<ISingleContentAnalyzer, SingleContentAnalyzer>();

        // Shared by the per-item and bulk delete paths so the purge fan-out lives in one place.
        services.AddScoped<ContentItemPurger>();

        // Module-neutral content-analysis profile seam (CAP2)
        services.AddScoped<IContentAnalysisProfile, SocialContentAnalysisProfile>();
        services.AddScoped<IContentAnalysisProfileProvider, ContentAnalysisProfileProvider>();

        // Content generation feature: all 7 format generators + orchestrator
        services.AddContentGeneration();

        return services;
    }
}
