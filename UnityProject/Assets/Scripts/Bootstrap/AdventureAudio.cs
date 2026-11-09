using UnityEngine;

namespace DNF60Prototype
{
    [DisallowMultipleComponent]
    public sealed class AdventureAudio : MonoBehaviour
    {
        public const float MusicVolume = .12f;
        public const float HitVolume = .65f;
        AudioSource music;
        AudioSource impact;
        AudioSource voice;
        AudioClip hurtSound;
        float nextHurtAt;
        AudioClip hitSound;
        AudioClip townMusic,battleMusic,bossMusic;
        void Awake()
        {
            music=gameObject.AddComponent<AudioSource>();
            music.playOnAwake=false;music.spatialBlend=0;
            music.volume=MusicVolume;music.loop=true;music.priority=128;
            townMusic=Load("town_variation");battleMusic=Load("battle_variation");bossMusic=Load("boss_variation");
            impact=gameObject.AddComponent<AudioSource>();
            impact.playOnAwake=false;impact.spatialBlend=0;
            impact.volume=HitVolume;impact.loop=false;impact.priority=64;
            hitSound=Resources.Load<AudioClip>("DNF60/Audio/Effects/hero_hit");
            voice=gameObject.AddComponent<AudioSource>();
            voice.playOnAwake=false;voice.spatialBlend=0;voice.volume=.48f;voice.priority=48;
            hurtSound=Resources.Load<AudioClip>("DNF60/Audio/Effects/hero_hurt");
            if(hurtSound==null)Debug.LogError("Missing hero hurt voice");
            if(hitSound==null)Debug.LogError("Missing hero hit sound");
        }
        AudioClip Load(string name)
        {
            var clip=Resources.Load<AudioClip>("DNF60/Audio/Music/"+name);
            if(clip==null)Debug.LogError("Missing original BGM: "+name);
            return clip;
        }
        public void SetDungeon(bool dungeon,bool boss=false)
        {
            var clip=dungeon?(boss?bossMusic:battleMusic):townMusic;
            if(music.clip==clip&&music.isPlaying)return;
            music.Stop();music.clip=clip;if(clip!=null)music.Play();
        }
        public void PlayHurt()
        {
            if(hurtSound==null||Time.unscaledTime<nextHurtAt)return;
            nextHurtAt=Time.unscaledTime+.45f;
            voice.PlayOneShot(hurtSound);
        }
        public void PlayHit()
        {
            if(hitSound!=null)impact.PlayOneShot(hitSound);
        }
    }
}
