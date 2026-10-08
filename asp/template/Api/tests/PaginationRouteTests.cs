namespace Api.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Mvc;

public class PaginationRouteTests
{
    [Theory]
#if (UseApiVersioning)
    [InlineData(typeof(TodoSample.Api.v2026_03_26.Controllers.TodosController))]
    [InlineData(typeof(TodoSample.Api.v2026_12_01.Controllers.TodosController))]
#else
    [InlineData(typeof(TodoSample.Api.Controllers.TodosController))]
#endif
    public void GetOverdue_AllApiVersions_UsesSharedPaginationRouteName(Type controllerType) =>
        controllerType.GetMethods().Single(method => method.Name == "GetOverdue")
            .GetCustomAttributes<HttpGetAttribute>()
            .Should().ContainSingle().Which.Name.Should().Be("Todos_GetOverdue");
}
