namespace PaperSeizerStone.Audio
{
    // Keep numeric values stable: the audio configuration serializes these IDs.
    public enum AudioCue
    {
        CardPickUp, CardDraw, CardPlace, CardReveal, CardShuffle, Bell,
        AmmunitionAdd, AmmunitionRemove, GunMechanism, GunLoad, GunEmpty,
        GunFire, Hit, ScoreTick, ScoreReveal, Victory, Defeat, ChipDrop, ChipPay, CardBurn,
        GunOpen, GunClose
    }

    public enum MusicContext { Menu, Battle, Shop }
}
