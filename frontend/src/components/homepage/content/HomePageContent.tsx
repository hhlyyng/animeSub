import AnimeFlow from "./AnimeInfoFlow";

const HomeContent = () => {
    const animeList = Array.from({ length: 10 }, (_, i) => ({
    id: `anime-${i}`,
    ch_title: `动漫标题 ${i + 1}`,
    en_title: `Anime Title ${i + 1}`,
    score: "8.5",
    images: {
      large: `https://picsum.photos/400/600?random=${i}`,
      medium: `https://picsum.photos/300/450?random=${i}`,
      small: `https://picsum.photos/200/300?random=${i}`,
      grid: `https://picsum.photos/300/450?random=${i}`,
      common: `https://picsum.photos/300/450?random=${i}`,
    },
  }));
    return (
        <div className="w-full px-12 overflow-x-hidden">
        <AnimeFlow topic="今日放送" items={animeList} />
        </div>
    )
};

export default HomeContent;