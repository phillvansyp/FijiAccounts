using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FijiAccounts.Web.Api.Mobile.V1;

public sealed class MobileClientOpenApiTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.Description.RelativePath?.StartsWith(
                MobileApiV1Endpoints.RoutePrefix.TrimStart('/'),
                StringComparison.Ordinal) != true)
        {
            return;
        }

        var stringSchema = await context.GetOrCreateSchemaAsync(
            typeof(string),
            parameterDescription: null,
            cancellationToken);
        operation.Parameters ??= [];
        AddHeader(operation, MobileClientEndpointFilter.PlatformHeader,
            "Mobile platform: ios or android.", stringSchema);
        AddHeader(operation, MobileClientEndpointFilter.VersionHeader,
            "Application version.", stringSchema);
        AddHeader(operation, MobileClientEndpointFilter.DeviceHeader,
            "Client-generated device installation UUID.", stringSchema);
    }

    private static void AddHeader(
        OpenApiOperation operation,
        string name,
        string description,
        IOpenApiSchema schema)
    {
        if (operation.Parameters?.Any(parameter =>
                parameter.Name == name && parameter.In == ParameterLocation.Header) == true)
        {
            return;
        }

        operation.Parameters!.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = true,
            Description = description,
            Schema = schema
        });
    }
}
