﻿using FoundationaLLM.Common.Models.Vectorization;

namespace FoundationaLLM.Common.Interfaces
{
    /// <summary>
    /// Provides text embedding capabilities.
    /// </summary>
    public interface ITextEmbeddingService
    {
        /// <summary>
        /// Initializes the text embedding operation.
        /// Depending on the implementation, this can be an atomic operation or a long-running one.
        /// </summary>
        /// <param name="textChunks">The list of text chunks which need to be embedded.</param>
        /// <returns>A <see cref="TextEmbeddingResult"/> object containing the result of the text embedding operation.</returns>
        Task<TextEmbeddingResult> GetEmbeddingsAsync(IList<TextChunk> textChunks);

        /// <summary>
        /// Retrieves the result of a long-running text embedding operation.
        /// </summary>
        /// <param name="operationId">The unique identifier of the long-running operation.</param>
        /// <returns>A <see cref="TextEmbeddingResult"/> object containing the result of the text embedding operation.</returns>
        Task<TextEmbeddingResult> GetEmbeddingsAsync(string operationId);
    }
}
