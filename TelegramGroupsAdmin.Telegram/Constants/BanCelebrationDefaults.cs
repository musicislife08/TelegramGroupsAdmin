namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Default ban celebration captions to seed on first startup.
/// Each entry contains (Name, ChatText, DmText).
/// ChatText uses {username}, {chatname}, {bancount} placeholders.
/// DmText is formatted for direct addressing ("You have been...").
/// </summary>
public static class BanCelebrationDefaults
{
    public static readonly IReadOnlyList<(string Name, string ChatText, string DmText)> Captions =
    [
        // Mortal Kombat (1-6)
        ("Mortal Kombat - Fatality", "💀 **FATALITY!** {username} has been finished!", "💀 You have been finished!"),
        ("Mortal Kombat - Toasty", "🔥 **TOASTY!** {username} got roasted!", "🔥 You got roasted!"),
        ("Mortal Kombat - Finish Him", "⚡ **FINISH HIM!** {username} won't be back!", "⚡ You won't be back!"),
        ("Mortal Kombat - Flawless Victory", "🩸 **FLAWLESS VICTORY!** {username} didn't stand a chance!", "🩸 You didn't stand a chance!"),
        ("Mortal Kombat - Brutality", "💀 **BRUTALITY!** {username} destroyed!", "💀 You've been destroyed!"),
        ("Mortal Kombat - Friendship", "🎭 **FRIENDSHIP?** Not for {username}!", "🎭 Not for you! Banned!"),

        // Street Fighter (7-10)
        ("Street Fighter - KO", "🥊 **K.O.!** {username} is down for the count!", "🥊 You're down for the count!"),
        ("Street Fighter - Perfect", "👊 **PERFECT!** {username} has been defeated!", "👊 You have been defeated!"),
        ("Street Fighter - Hadouken", "🌀 **HADOUKEN!** {username} blasted out of {chatname}!", "🌀 You've been blasted out!"),
        ("Street Fighter - Shoryuken", "⬆️ **SHORYUKEN!** {username} launched into orbit!", "⬆️ You've been launched into orbit!"),

        // FPS/Shooters (11-16)
        ("FPS - Headshot", "💥 **HEADSHOT!** {username} eliminated!", "💥 You've been eliminated!"),
        ("FPS - 360 No-Scope", "🎯 **360 NO-SCOPE!** {username} didn't see it coming!", "🎯 You didn't see it coming!"),
        ("FPS - Boom Headshot", "💣 **BOOM! HEADSHOT!** {username} rekt!", "💣 You got rekt!"),
        ("FPS - Killtacular", "☠️ **KILLTACULAR!** {username} owned!", "☠️ You got owned!"),
        ("FPS - Enemy Down", "🔫 **ENEMY DOWN!** {username} neutralized!", "🔫 You've been neutralized!"),
        ("FPS - Mission Complete", "🎖️ **MISSION COMPLETE!** {username} has been extracted... permanently!", "🎖️ You've been extracted... permanently!"),

        // Classic Gaming (17-22)
        ("Classic - Game Over", "🎮 **GAME OVER** for {username}! Insert coin to try again... just kidding.", "🎮 Insert coin to try again... just kidding."),
        ("GTA - Wasted", "👻 **WASTED!** {username} has left the chat permanently.", "👻 You have left the chat permanently."),
        ("Zero Wing - All Your Base", "🚀 **ALL YOUR BASE ARE BELONG TO US!** Goodbye {username}!", "🚀 All your base are belong to us! Goodbye!"),
        ("Mario - Game Over", "🍄 **GAME OVER!** {username} ran out of lives!", "🍄 You ran out of lives!"),
        ("Smash Bros - Star KO", "⭐ **STAR KO!** {username} blasted off the stage!", "⭐ You've been blasted off the stage!"),
        ("Metal Gear - Snake", "🐍 **SNAKE? SNAKE?! SNAAAAKE!** {username} has fallen!", "🐍 You have fallen!"),

        // Dark Souls/RPG (23-26)
        ("Dark Souls - You Died", "⚔️ **YOU DIED** ...just kidding, {username} got banned!", "⚔️ YOU DIED... just kidding, you got banned!"),
        ("Dark Souls - Vanquished", "🛡️ **{username} HAS BEEN VANQUISHED!** Git gud.", "🛡️ YOU HAVE BEEN VANQUISHED! Git gud."),
        ("RPG - Quest Complete", "📜 **QUEST COMPLETE:** Ban {username} ✓", "📜 QUEST COMPLETE: Ban you ✓"),
        ("Dark Souls - Gone Hollow", "⚰️ **{username} HAS GONE HOLLOW!** No more spam for you!", "⚰️ YOU HAVE GONE HOLLOW! No more spam!"),

        // Ban Hammer Classics (27-32)
        ("Ban Hammer - Classic", "🔨 **BAN HAMMER!** Another spammer bites the dust!", "🔨 You bit the dust!"),
        ("Ban Hammer - Hammered", "⚒️ **HAMMERED!** {username} got the ban they deserved!", "⚒️ You got the ban you deserved!"),
        ("Ban Hammer - Bonk", "🛠️ **BONK!** {username} sent to spam jail!", "🛠️ You've been sent to spam jail!"),
        ("Security - Access Denied", "⛔ **ACCESS DENIED!** {username} has been removed!", "⛔ ACCESS DENIED! You've been removed!"),
        ("Security - No Spammers", "🚷 **NO SPAMMERS ALLOWED!** {username} evicted!", "🚷 NO SPAMMERS ALLOWED! You've been evicted!"),
        ("Security - Locked Out", "🔐 **LOCKED OUT!** {username} lost their privileges!", "🔐 LOCKED OUT! You lost your privileges!"),

        // Pop Culture (33-40)
        ("LOTR - Shall Not Pass", "🚪 **YOU SHALL NOT PASS!** {username} has been cast out!", "🚪 You have been cast out!"),
        ("Meme - Yeet", "👋 **YEET!** {username} has been yeeted from {chatname}!", "👋 You have been yeeted!"),
        ("Pokemon - Team Rocket", "🌟 **TEAM ROCKET'S BLASTING OFF AGAIN!** Bye {username}!", "🌟 That's you blasting off!"),
        ("Karate Kid - Sweep the Leg", "🧹 **SWEEP THE LEG!** {username} has been swept away!", "🧹 You have been swept away!"),
        ("Star Trek - Beamed Out", "🖖 **LIVE LONG AND... NOT HERE!** {username} beamed out!", "🖖 You've been beamed out!"),
        ("Lion King - Hakuna Matata", "🦁 **HAKUNA MATATA!** No more worries about {username}!", "🦁 No more worries about you here!"),
        ("Joker - Why So Serious", "🃏 **WHY SO SERIOUS?** {username} got the last laugh... NOT!", "🃏 You didn't get the last laugh!"),
        ("Harry Potter - Expelliarmus", "🧙 **EXPELLIARMUS!** {username} disarmed and banned!", "🧙 You've been disarmed and banned!"),

        // Duke Nukem / They Live (41)
        ("Duke Nukem - Bubblegum", "🕶️ **I'M HERE TO BAN SPAMMERS AND CHEW BUBBLEGUM...** and I'm all out of bubblegum. Bye {username}!", "🕶️ I'm all out of bubblegum. Bye!"),

        // Sarcastic/Witty (42-47)
        ("Sarcastic - Trash", "🗑️ **TAKING OUT THE TRASH!** {username} disposed of properly.", "🗑️ You've been disposed of properly."),
        ("Magic - Disappears", "🎪 **AND FOR MY NEXT TRICK...** {username} disappears forever!", "🎪 You disappear forever!"),
        ("Sarcastic - Consequences", "👀 **OOPS!** {username} just learned actions have consequences!", "👀 You just learned actions have consequences!"),
        ("Transport - Ban Train", "🚂 **ALL ABOARD THE BAN TRAIN!** {username} is today's passenger!", "🚂 You're today's passenger!"),
        ("Poetry - Roses", "📝 **ROSES ARE RED, VIOLETS ARE BLUE,** {username} got banned, boo hoo!", "📝 You got banned, boo hoo!"),
        ("Queen - Another One", "🎵 **ANOTHER ONE BITES THE DUST!** Bye {username}!", "🎵 Another one bites the dust! That's you!"),

        // With Ban Counter (48-51)
        ("Counter - Ban Number", "🔨 **BAN #{bancount} TODAY!** {username} added to the pile!", "🔨 You've been added to the pile!"),
        ("Counter - Daily Score", "📊 **DAILY SCORE: {bancount}** — {username} didn't make the cut!", "📊 You didn't make the cut!"),
        ("Counter - Spammer Number", "🏆 **SPAMMER #{bancount}!** Keep 'em coming, we're on a roll!", "🏆 That's you! We're on a roll!"),
        ("Counter - X Down", "⚡ **{bancount} DOWN!** {username} joins today's banned club!", "⚡ You've joined today's banned club!")
    ];
}
