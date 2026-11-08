using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The level-up screen: pauses the game and offers CardCount random skills
/// as cards (rolled by SkillManager). Picking one learns it and resumes.
///
/// Several level-ups at once (one big XP pickup) are queued: after each
/// pick the next set of cards is rolled, until all are used. If nothing
/// can be learned any more, level-ups just pass without the screen.
///
/// Like the pause menu, it runs with ProcessMode Always (set in
/// level_up_screen.tscn) so it keeps working while the game is paused.
/// </summary>
public partial class LevelUpScreen : CanvasLayer
{
    [Export] public PlayerStats Stats;
    [Export] public SkillManager Skills;
    [Export] public PackedScene CardScene;

    [Export(PropertyHint.Range, "1,5,1")] public int CardCount = 3;

    // Seconds after the screen opens before the cards accept taps, so a
    // finger still on the joystick (or a tap meant for the game) doesn't
    // pick a card by accident.
    [Export] public float InputDelay = 0.4f;

    private SkillCard[] _cards;
    private SkillDefinition[] _choices;
    private int _pendingLevelUps;

    public override void _Ready()
    {
        Hide();

        // Make the cards once and reuse them for every level-up.
        Container cardRow = GetNode<Container>("%Cards");
        _cards = new SkillCard[CardCount];
        _choices = new SkillDefinition[CardCount];
        for (int i = 0; i < CardCount; i++)
        {
            SkillCard card = CardScene.Instantiate<SkillCard>();
            cardRow.AddChild(card);
            card.Chosen += OnCardChosen;
            _cards[i] = card;
        }

        if (Stats != null)
        {
            Stats.LeveledUp += OnLeveledUp;
        }
    }

    private void OnLeveledUp(int newLevel)
    {
        _pendingLevelUps++;
        // Already showing cards: this one waits its turn.
        if (!Visible)
        {
            ShowNextChoices();
        }
    }

    private void ShowNextChoices()
    {
        int count = _pendingLevelUps > 0 && Skills != null ? Skills.RollChoices(_choices) : 0;
        if (count == 0)
        {
            // Nothing (more) to offer.
            _pendingLevelUps = 0;
            Close();
            return;
        }

        for (int i = 0; i < _cards.Length; i++)
        {
            bool used = i < count;
            _cards[i].Visible = used;
            if (used)
            {
                _cards[i].SetSkill(_choices[i], Skills.GetLevel(_choices[i]));
            }
        }

        SetCardsEnabled(false);
        // processAlways true: this timer must run while the game is paused.
        GetTree().CreateTimer(InputDelay, true).Timeout += () => SetCardsEnabled(true);

        Show();
        GetTree().Paused = true;
    }

    private void SetCardsEnabled(bool enabled)
    {
        foreach (SkillCard card in _cards)
        {
            card.Disabled = !enabled;
        }
    }

    private void OnCardChosen(SkillDefinition skill)
    {
        Skills.Learn(skill);
        _pendingLevelUps--;
        ShowNextChoices();
    }

    private void Close()
    {
        Hide();
        GetTree().Paused = false;
    }
}
