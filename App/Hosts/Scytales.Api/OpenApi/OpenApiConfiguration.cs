using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scytales.Api.Contracts;

namespace Scytales.Api.OpenApi;

internal static class OpenApiConfiguration
{
    private const string SubmitOperationId = "SubmitApplication";
    private const string FormUrlEncoded = "application/x-www-form-urlencoded";

    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Driving Licence Onboarding API",
                Version = "v1",
                Description = "Submit driving licence applications, follow their asynchronous workflow and decide manual reviews."
            };
            return Task.CompletedTask;
        });

        // A nullable IFormFile is generated as oneOf[null, IFormFile]; Swagger UI only offers a file picker
        // for a plain binary string.
        options.AddSchemaTransformer((schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type == typeof(SubmitApplicationForm) && schema.Properties is { } properties)
            {
                properties["photo"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = "binary",
                    Description = "Face photo, JPEG or PNG. Optional here: a missing photo fails the workflow's photo check."
                };
            }
            return Task.CompletedTask;
        });

        // [FromForm] also advertises url-encoded forms, which cannot carry the photo.
        options.AddOperationTransformer((operation, _, _) =>
        {
            if (operation.OperationId == SubmitOperationId)
                operation.RequestBody?.Content?.Remove(FormUrlEncoded);
            return Task.CompletedTask;
        });
    }
}
