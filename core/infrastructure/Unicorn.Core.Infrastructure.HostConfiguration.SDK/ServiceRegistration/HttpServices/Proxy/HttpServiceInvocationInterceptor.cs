﻿using System.Text.Json;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;

namespace Unicorn.Core.Infrastructure.HostConfiguration.SDK.ServiceRegistration.HttpServices.Proxy;

internal class HttpServiceInvocationInterceptor : IInterceptor
{
    private readonly ILogger _logger;
    private readonly Type _taskType = typeof(Task);
    private readonly IRestComponentProvider _restComponentProvider;

    public HttpServiceInvocationInterceptor(
        IRestComponentProvider restComponentProvider,
        ILogger<HttpServiceInvocationInterceptor> logger)
    {
        _restComponentProvider = restComponentProvider;
        _logger = logger;
    }

    public void Intercept(IInvocation invocation)
    {
        var returnType = invocation.Method.ReturnType;

        if (returnType.BaseType == _taskType)
        {
            ExecuteGenericTaskReturnTypeInvocation(invocation);
        }
        else if (returnType == _taskType)
        {
            invocation.ReturnValue = ExecuteTaskReturnTypeInvocationAsync(invocation);
        }
    }

    private async Task ExecuteTaskReturnTypeInvocationAsync(IInvocation invocation)
    {
        var client = _restComponentProvider.GetRestClientAsync(invocation.Method.DeclaringType!);
        var request = _restComponentProvider.GetRestRequestAsync(invocation.Method, invocation.Arguments);

        await Task.WhenAll(client, request);

        await client.Result.ExecuteAsync(request.Result);
    }

    private void ExecuteGenericTaskReturnTypeInvocation(IInvocation invocation)
    {
        var returnType = invocation.Method.ReturnType;

        var tcsType = typeof(TaskCompletionSource<>).MakeGenericType(returnType.GetGenericArguments()[0]);
        var tcs = Activator.CreateInstance(tcsType);
        invocation.ReturnValue = tcsType.GetProperty("Task")!.GetValue(tcs, null);

        ExecuteGenericTaskReturnTypeInvocationAsync(invocation).ContinueWith(_ =>
        {
            tcsType.GetMethod("SetResult")!.Invoke(tcs, new object[] { invocation.ReturnValue! });
        });
    }

    private async Task ExecuteGenericTaskReturnTypeInvocationAsync(IInvocation invocation)
    {
        var client = _restComponentProvider.GetRestClientAsync(invocation.Method.DeclaringType!);
        var request = _restComponentProvider.GetRestRequestAsync(invocation.Method, invocation.Arguments);

        await Task.WhenAll(client, request);

        var response = await client.Result.ExecuteAsync(request.Result);

        // TODO: add response validation, if statusCode 404, 503 etc.

        var result = JsonSerializer.Deserialize(
           response.Content!,
           invocation.Method.ReturnType.GenericTypeArguments.First(),
           new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        invocation.ReturnValue = result;
    }
}
