using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proposal.Controllers;

namespace Proposal.Tests;

public sealed class MvcSecurityConventionTests
{
    [Theory]
    [InlineData(typeof(AiRecommendationController))]
    [InlineData(typeof(CalculatorController))]
    [InlineData(typeof(EquipmentController))]
    [InlineData(typeof(LolAramAugmentsController))]
    [InlineData(typeof(LolAramGuidesController))]
    [InlineData(typeof(UserController))]
    public void ProtectedController_RequiresAuthenticatedUser(Type controllerType)
    {
        Assert.NotEmpty(controllerType.GetCustomAttributes<AuthorizeAttribute>());
    }

    [Fact]
    public void EveryHttpPostAction_UsesAntiforgeryValidation()
    {
        var unprotectedActions = ControllerTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .Where(method => method.GetCustomAttribute<HttpPostAttribute>() is not null)
            .Where(method => method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is null)
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .OrderBy(name => name)
            .ToArray();

        Assert.True(
            unprotectedActions.Length == 0,
            $"HttpPost actions missing antiforgery validation: {string.Join(", ", unprotectedActions)}");
    }

    [Theory]
    [InlineData(typeof(EquipmentController), "Create")]
    [InlineData(typeof(EquipmentController), "Edit")]
    [InlineData(typeof(EquipmentController), "Delete")]
    [InlineData(typeof(EquipmentController), "ImportExcel")]
    [InlineData(typeof(EquipmentController), "ImportLeagueItemsFromDataDragon")]
    [InlineData(typeof(LolAramAugmentsController), "Create")]
    [InlineData(typeof(LolAramAugmentsController), "Edit")]
    [InlineData(typeof(LolAramAugmentsController), "Delete")]
    [InlineData(typeof(LolAramAugmentsController), "Import")]
    [InlineData(typeof(LolAramAugmentsController), "ImportFromOpGg")]
    [InlineData(typeof(LolAramGuidesController), "Create")]
    [InlineData(typeof(LolAramGuidesController), "Edit")]
    [InlineData(typeof(LolAramGuidesController), "Delete")]
    [InlineData(typeof(LolAramGuidesController), "ImportChampionAugmentsFromOpGg")]
    public void AdministrativeAction_RequiresAdminRole(Type controllerType, string actionName)
    {
        var methods = controllerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == actionName)
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            var attributes = method.GetCustomAttributes<AuthorizeAttribute>().ToArray();
            Assert.Contains(attributes, attribute =>
                (attribute.Roles ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains("Admin", StringComparer.Ordinal));
        });
    }

    private static IEnumerable<Type> ControllerTypes()
    {
        return typeof(HomeController).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(Controller).IsAssignableFrom(type));
    }
}
