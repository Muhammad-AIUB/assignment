using Api.Controllers;
using Api.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace UnitTests;

/// <summary>ClassesController with a test double behind it: it passes the request through and maps the result to HTTP.</summary>
public class ClassesControllerTests
{
    private readonly FakeClassService _service = new();
    private readonly ClassesController _controller;

    public ClassesControllerTests() => _controller = new ClassesController(_service);

    [Fact]
    public async Task Create_passes_the_request_and_token_through_and_answers_201_pointing_at_the_new_class()
    {
        var created = new ClassResponse("c_123", "Class 6 - Morning Shift", 120);
        _service.CreateResult = created;
        var request = new CreateClassRequest("Class 6 - Morning Shift", 120);
        using var cts = new CancellationTokenSource();

        var result = Assert.IsType<CreatedAtActionResult>(await _controller.Create(request, cts.Token));

        Assert.Equal(201, result.StatusCode);
        Assert.Same(created, result.Value);
        Assert.Equal(nameof(ClassesController.Get), result.ActionName);
        Assert.Equal("c_123", result.RouteValues!["classId"]);

        var call = Assert.Single(_service.Calls);
        Assert.Same(request, call.Arg);
        Assert.Equal(cts.Token, call.Token);
    }

    [Fact]
    public async Task List_returns_whatever_the_service_returns_and_an_empty_list_is_still_200()
    {
        var empty = Assert.IsType<OkObjectResult>(await _controller.List(default));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ClassSummaryResponse>>(empty.Value));

        IReadOnlyList<ClassSummaryResponse> classes = [new("c_1", "Class 6", 120, 15, 80, 25)];
        _service.ListResult = classes;
        var full = Assert.IsType<OkObjectResult>(await _controller.List(default));
        Assert.Equal(200, full.StatusCode);
        Assert.Same(classes, full.Value);
    }

    [Fact]
    public async Task Get_returns_the_class_the_service_found()
    {
        var found = new ClassSummaryResponse("c_1", "Class 6", 120, 15, 80, 25);
        _service.GetResult = found;
        using var cts = new CancellationTokenSource();

        var result = Assert.IsType<OkObjectResult>(await _controller.Get("c_1", cts.Token));

        Assert.Same(found, result.Value);
        var call = Assert.Single(_service.Calls);
        Assert.Equal("c_1", call.Arg);
        Assert.Equal(cts.Token, call.Token);
    }

    [Fact]
    public async Task Get_of_an_unknown_class_is_404_CLASS_NOT_FOUND_naming_the_class()
    {
        _service.GetResult = null;

        var result = Assert.IsType<NotFoundObjectResult>(await _controller.Get("c_missing", default));

        var error = Assert.IsType<ErrorResponse>(result.Value);
        Assert.Equal("CLASS_NOT_FOUND", error.Error);
        Assert.Contains("c_missing", error.Message);
    }
}
