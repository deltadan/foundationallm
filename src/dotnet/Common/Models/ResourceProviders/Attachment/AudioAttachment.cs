﻿using FoundationaLLM.Common.Constants.ResourceProviders;
using System.Text.Json.Serialization;

namespace FoundationaLLM.Common.Models.ResourceProviders.Attachment
{
    /// <summary>
    /// Audio attachment.
    /// </summary>
    public class AudioAttachment : AttachmentBase
    {
        /// <summary>
        /// Stream of binary audio file from the Core API
        /// </summary>
        public Stream? Content { get; set; }

        /// <summary>
        /// Creates a new instance of the <see cref="AudioAttachment"/> attachment.
        /// </summary>
        public AudioAttachment() =>
            Type = AttachmentTypes.Audio;
    }
}
