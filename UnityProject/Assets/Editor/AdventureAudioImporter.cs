using UnityEditor;
using UnityEngine;

public sealed class AdventureAudioImporter : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if(!assetPath.StartsWith("Assets/Resources/DNF60/Audio/"))return;
        var importer=(AudioImporter)assetImporter;
        var settings=importer.defaultSampleSettings;
        bool music=assetPath.Contains("/Music/");
        settings.loadType=music?AudioClipLoadType.Streaming:AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.quality=.85f;
        settings.preloadAudioData=!music;importer.defaultSampleSettings=settings;
    }
}
