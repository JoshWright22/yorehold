#include <yorehold/framework/Host.h>

#include <SDL3/SDL.h>
#include <SDL3/SDL_main.h>

class YoreholdGame : public yh::Game
{
public:
    void draw(SDL_Renderer* renderer) override
    {
        SDL_SetRenderDrawColor(renderer, 14, 18, 32, 255);
        SDL_RenderClear(renderer);
    }
};

int main(int, char**)
{
    YoreholdGame game;
    return yh::run(game);
}
