import React, { useState } from "react";

export type AnimeInfo = {
  id: string;
  ch_title: string;
  en_title: string;
  ch_desc?: string;
  en_desc?: string;
  score: string;
  images: {
    large: string;
    medium: string;
    small: string;
    grid: string;
    common: string;
  };
};

type AnimeCardProps = {
  anime: AnimeInfo;
};


export function AnimeCard( { anime }: AnimeCardProps) {
  const [open, setOpen] = useState(false);

  return (
    <div
      className="
                relative cursor-pointer select-none
                transition-transform duration-300
                hover:scale-110 hover:z-10
                "
      onClick={()=> {setOpen(true)}}
    >
      {/* Cover */}
      <div className="relative w-full aspect-[2/3] overflow-hidden bg-gray-100">
        <img
          src={anime.images.medium}
          alt={anime.ch_title}
          className="absolute inset-0 h-full w-full object-cover"
        />
      </div>

      {/* title + score*/}
      <div className="mt-2 text-sm font-semibold text-gray-900 line-clamp-2">
        {anime.ch_title}
      </div>
    </div>
  );
}

export default AnimeCard;
