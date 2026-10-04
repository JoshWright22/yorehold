#pragma once

#include <yorehold/framework/rpg/CharacterChoices.h>
#include <yorehold/framework/rpg/Compendium.h>

#include <map>
#include <optional>
#include <string>
#include <vector>

// A character being made, or one more level being added, with its sheet rebuilt after every
// change so the screens can show it live. Everything the player can pick comes from the ruleset
// and the compendium; anything the rules refuse shows up in `problem` instead of a sheet.
class CharacterDraft
{
public:
    // Making a character: origin (name, race, background), then class and scores, then the first
    // level's picks.
    static constexpr int steps = 3;
    static constexpr const char* stepNames[steps] = {"Origin", "Class and scores", "Skills and feats"};

    CharacterDraft(const yh::Ruleset& rules, const yh::Compendium& compendium);
    // One more level for an existing character, going into its latest class to start with.
    static CharacterDraft levelUp(const yh::Ruleset& rules, const yh::Compendium& compendium, yh::CharacterChoices choices);

    const yh::CharacterChoices& choices() const { return choices_; }
    const std::optional<yh::Character>& sheet() const { return sheet_; }
    const std::string& problem() const { return problem_; }
    bool levellingUp() const { return levellingUp_; }

    int step = 0;
    // Whether the step's choices are complete; making the character needs all of them.
    bool stepDone(int which) const;
    std::string stepProblem(int which) const;
    bool finished() const;

    void setName(std::string name);
    void setRace(std::string id);
    void setBackground(std::string id);
    // The class of the level being chosen (the first when making a character). Its picks start over.
    void setClass(std::string id);

    // Scores. Switching method starts the scores over: rolled afresh, the array in ability order,
    // or every score at the cheapest the point buy allows.
    void setMethod(const std::string& method, yh::Random& random);
    void reroll(yh::Random& random);
    // Point buy: the next score the cost table lists. Array: swaps with the ability holding the
    // next value. Not for rolled scores.
    bool canRaise(const std::string& ability) const;
    bool canLower(const std::string& ability) const;
    void raise(const std::string& ability);
    void lower(const std::string& ability);
    int pointsLeft() const;

    // The picks of the level being chosen.
    int skillPicks() const;                    // how many skills it trains
    std::vector<std::string> featKinds() const; // the kinds of feat it offers, one each
    const std::vector<std::string>& skillOptions() const { return skillOptions_; } // skills not trained already
    // Feats of that kind the character could take now (their requirements met, not taken before).
    std::vector<std::string> featOptions(const std::string& kind) const;
    const std::vector<std::string>& picked(const std::string& kind) const;
    // Skills: on or off, up to skillPicks(). Feats: the one of its kind (another of the same kind
    // replaces it).
    void toggleSkill(const std::string& id);
    void pickFeat(const std::string& id);

    // Sorted by name, for the screens.
    std::vector<std::string> raceIds() const;
    std::vector<std::string> backgroundIds() const;
    std::vector<std::string> classIds() const;

private:
    const yh::Ruleset& rules_;
    const yh::Compendium& compendium_;
    yh::CharacterChoices choices_;
    std::optional<yh::Character> sheet_;
    std::string problem_;
    std::vector<std::string> skillOptions_; // worked out with the sheet, without this level's skill picks
    std::map<std::string, std::vector<std::string>> featOptions_; // by kind: the feats a trial build accepts
    bool levellingUp_ = false;

    yh::LevelChoice& level() { return choices_.levels.back(); }
    const yh::LevelChoice& level() const { return choices_.levels.back(); }
    const yh::ClassLevel* row() const;
    std::vector<int> arrayValues() const;
    void rebuild();
};
