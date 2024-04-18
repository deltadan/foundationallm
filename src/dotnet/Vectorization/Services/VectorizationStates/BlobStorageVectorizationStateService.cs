using FoundationaLLM.Common.Constants.Configuration;
using FoundationaLLM.Common.Interfaces;
using FoundationaLLM.Common.Models.ResourceProviders.Vectorization;
using FoundationaLLM.Common.Services;
using FoundationaLLM.Vectorization.Interfaces;
using FoundationaLLM.Vectorization.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace FoundationaLLM.Vectorization.Services.VectorizationStates
{
    /// <summary>
    /// Provides vectorization state persistence services using  Azure blob storage.
    /// </summary>
    /// <remarks>
    /// Creates a new vectorization state service instance.
    /// </remarks>
    /// <param name="storageService">The <see cref="IStorageService"/> that provides storage services.</param>
    /// <param name="loggerFactory">The logger factory used to create loggers.</param>
    public class BlobStorageVectorizationStateService(
        [FromKeyedServices(DependencyInjectionKeys.FoundationaLLM_Vectorization_BlobStorageVectorizationStateService)] IStorageService storageService,
        ILoggerFactory loggerFactory) : VectorizationStateServiceBase, IVectorizationStateService
    {
        private readonly IStorageService _storageService = storageService;
        private readonly ILoggerFactory _loggerFactory = loggerFactory;

        private const string BLOB_STORAGE_CONTAINER_NAME = "vectorization-state";
        private const string EXECUTION_STATE_DIRECTORY = "execution-state";
        private const string PIPELINE_STATE_DIRECTORY = "pipeline-state";

        /// <inheritdoc/>
        public async Task<bool> HasState(VectorizationRequest request) =>
            await _storageService.FileExistsAsync(
                BLOB_STORAGE_CONTAINER_NAME,
                $"{EXECUTION_STATE_DIRECTORY}/{GetPersistenceIdentifier(request.ContentIdentifier)}.json",
                default);


        /// <inheritdoc/>
        public async Task<VectorizationState> ReadState(VectorizationRequest request)
        {
            var content = await _storageService.ReadFileAsync(
                BLOB_STORAGE_CONTAINER_NAME,
                $"{EXECUTION_STATE_DIRECTORY}/{GetPersistenceIdentifier(request.ContentIdentifier)}.json",
                default);

            return JsonSerializer.Deserialize<VectorizationState>(content)!;
        }

        /// <inheritdoc/>
        public async Task LoadArtifacts(VectorizationState state, VectorizationArtifactType artifactType)
        {
            foreach (var artifact in state.Artifacts.Where(a => a.Type == artifactType))
                if (!string.IsNullOrWhiteSpace(artifact.CanonicalId))
                    artifact.Content = Encoding.UTF8.GetString(
                        await _storageService.ReadFileAsync(
                            BLOB_STORAGE_CONTAINER_NAME,
                            artifact.CanonicalId, //artifact canonical id contains the execution-state directory in path
                            default));
        }

        /// <inheritdoc/>
        public async Task SaveState(VectorizationState state)
        {
            var persistenceIdentifier = GetPersistenceIdentifier(state.ContentIdentifier);

            foreach (var artifact in state.Artifacts)
                if (artifact.IsDirty)
                {
                    var artifactPath =
                        $"{EXECUTION_STATE_DIRECTORY}/{persistenceIdentifier}_{artifact.Type.ToString().ToLower()}_{artifact.Position:D6}.txt";

                    await _storageService.WriteFileAsync(
                        BLOB_STORAGE_CONTAINER_NAME,
                        artifactPath,
                        artifact.Content!,
                        default,
                        default);
                    artifact.CanonicalId = artifactPath;
                }

            var content = JsonSerializer.Serialize(state);
            await _storageService.WriteFileAsync(
                BLOB_STORAGE_CONTAINER_NAME,
                $"{EXECUTION_STATE_DIRECTORY}/{persistenceIdentifier}.json",
                content,
                default,
                default);
        }

        /// <inheritdoc/>
        public async Task SavePipelineState(VectorizationPipelineState state)
        {
            //pipeline object id format: "/instances/{instanceId}/providers/FoundationaLLM.Vectorization/vectorizationPipelines/{pipeline-name}"
            var pipelineName = state.PipelineObjectId.Split('/').Last();
            //vectorization-state/pipeline-state/pipeline-name/pipeline-name-pipeline-execution-id.json
            var pipelineStatePath = $"{PIPELINE_STATE_DIRECTORY}/{pipelineName}/{pipelineName}-{state.ExecutionId}.json";
            var content = JsonSerializer.Serialize(state);
            await _storageService.WriteFileAsync(
                BLOB_STORAGE_CONTAINER_NAME,
                pipelineStatePath,
                content,
                default,
                default);            
        }

        /// <inheritdoc/>
        public async Task<VectorizationPipelineState> ReadPipelineState(string pipelineName, string pipelineExecutionId)
        {
            var pipelineStatePath = $"{PIPELINE_STATE_DIRECTORY}/{pipelineName}/{pipelineName}-{pipelineExecutionId}.json";
            var content = await _storageService.ReadFileAsync(
                BLOB_STORAGE_CONTAINER_NAME,
                pipelineStatePath,
                default);

            return JsonSerializer.Deserialize<VectorizationPipelineState>(content)!;

        }
    }
}
