"""Things you can ask Ember to make, built from real CC0 models (Kenney; Quaternius via poly.pizza).
One table drives three outputs: the models in kit.bytes (build-kit.py), the app's word list
(Assets/Worldeck/Scripts/Core/MadeKinds.cs) and the relay's library (proxy/made-kinds.js).

kind: (size in metres when made with no size said, [words that name it], [(pack, model)])
pack "glb:<folder>" reads <folder>/<model>.glb; otherwise a Kenney pack's OBJ."""

PP = "glb:polypizza"
PETS = "glb:cube-pets/x/Models/GLB format"
PEOPLE = "glb:mini-characters/x/Models/GLB format"
# The people a "person" can be, in this order: Locals.cs picks one by its place in this list (LocalDef.Seed), so add
# new ones at the end. 0 dark hair, purple top; 1 glasses, green T-shirt; 2 ginger bun, dark suit; 3 police officer;
# 4 blonde buns, yellow jacket; 5 beanie, sporty; 6 white coat; 7 ponytail, overalls; 8 bald, beard; 9 black suit and
# tie; 10 glasses, white coat; 11 dark hair, green shirt; 12 beard and eye patch; 13 hard hat; 14 green vest.
PERSONS = ["character-female-a", "character-male-a", "character-female-d", "character-male-c", "character-female-b",
           "character-female-c", "character-female-e", "character-female-f", "character-male-b", "character-male-d",
           "character-male-e", "character-male-f", "character-male-g", "character-worker", "character-raider"]

MADE = {
    # ---- dinosaurs (Quaternius) ----
    "dinosaur": (0.5, ["dinosaur", "dino", "dinosaurs"], [(PP + "/dinosaurs", n) for n in ["T-Rex", "Triceratops", "Stegosaurus", "Velociraptor", "Parasaurolophus", "Apatosaurus"]]),
    "t_rex": (0.5, ["t rex", "trex", "t-rex", "tyrannosaurus", "tyrannosaurus rex"], [(PP + "/dinosaurs", "T-Rex")]),
    "triceratops": (0.45, ["triceratops"], [(PP + "/dinosaurs", "Triceratops")]),
    "stegosaurus": (0.5, ["stegosaurus"], [(PP + "/dinosaurs", "Stegosaurus")]),
    "velociraptor": (0.35, ["velociraptor", "raptor"], [(PP + "/dinosaurs", "Velociraptor")]),
    "parasaurolophus": (0.45, ["parasaurolophus"], [(PP + "/dinosaurs", "Parasaurolophus")]),
    "brontosaurus": (0.6, ["brontosaurus", "apatosaurus", "long neck dinosaur", "diplodocus"], [(PP + "/dinosaurs", "Apatosaurus")]),
    # ---- animals (Quaternius) ----
    "fox": (0.3, ["fox"], [(PP + "/animals", "Fox")]),
    "wolf": (0.35, ["wolf"], [(PP + "/animals", "Wolf")]),
    "deer": (0.4, ["deer", "doe", "fawn"], [(PP + "/animals", "Deer")]),
    "stag": (0.45, ["stag", "reindeer", "elk", "moose"], [(PP + "/animals", "Stag")]),
    "horse": (0.45, ["horse", "pony", "white horse"], [(PP + "/animals", "Horse"), (PP + "/animals", "White_Horse")]),
    "cow": (0.4, ["cow", "cattle"], [(PP + "/animals", "Cow")]),
    "bull": (0.4, ["bull", "ox"], [(PP + "/animals", "Bull")]),
    "alpaca": (0.35, ["alpaca"], [(PP + "/animals", "Alpaca")]),
    "llama": (0.35, ["llama"], [(PP + "/farm", "Llama")]),
    "donkey": (0.35, ["donkey", "mule"], [(PP + "/animals", "Donkey")]),
    "dog": (0.28, ["dog", "puppy", "doggy", "husky", "shiba", "shiba inu", "pug"], [(PP + "/animals", "Husky"), (PP + "/animals", "Shiba_Inu"), (PP + "/farm", "Pug")]),
    "sheep": (0.3, ["sheep", "lamb"], [(PP + "/farm", "Sheep")]),
    "pig": (0.28, ["pig", "piggy", "piglet", "hog"], [(PP + "/farm", "Pig")]),
    "zebra": (0.4, ["zebra"], [(PP + "/farm", "Zebra")]),
    # ---- cube pets (Kenney) ----
    "lion": (0.3, ["lion", "lioness"], [(PETS, "animal-lion")]),
    "tiger": (0.3, ["tiger"], [(PETS, "animal-tiger")]),
    "bee": (0.2, ["bee", "bumblebee", "honeybee"], [(PETS, "animal-bee")]),
    "cat": (0.22, ["cat", "kitten", "kitty"], [(PETS, "animal-cat")]),
    "bunny": (0.2, ["bunny", "rabbit"], [(PETS, "animal-bunny")]),
    "elephant": (0.4, ["elephant"], [(PETS, "animal-elephant")]),
    "giraffe": (0.45, ["giraffe"], [(PETS, "animal-giraffe")]),
    "monkey": (0.25, ["monkey", "ape", "chimp"], [(PETS, "animal-monkey")]),
    "panda": (0.3, ["panda", "panda bear"], [(PETS, "animal-panda")]),
    "polar_bear": (0.35, ["polar bear"], [(PETS, "animal-polar")]),
    "koala": (0.22, ["koala"], [(PETS, "animal-koala")]),
    "penguin": (0.22, ["penguin"], [(PETS, "animal-penguin")]),
    "parrot": (0.2, ["parrot", "bird"], [(PETS, "animal-parrot")]),
    "chick": (0.15, ["chick", "chicken", "duckling"], [(PETS, "animal-chick")]),
    "crab": (0.18, ["crab"], [(PETS, "animal-crab")]),
    "fish": (0.2, ["fish", "goldfish"], [(PETS, "animal-fish")]),
    "beaver": (0.22, ["beaver"], [(PETS, "animal-beaver")]),
    "caterpillar": (0.2, ["caterpillar", "worm"], [(PETS, "animal-caterpillar")]),
    # ---- people and characters (Kenney Mini Characters) ----
    "person": (0.3, ["person", "friend", "someone", "human", "man", "woman", "girl", "boy"], [(PEOPLE, n) for n in PERSONS]),
    "knight": (0.3, ["knight"], [(PEOPLE, "character-knight")]),
    "robot": (0.3, ["robot"], [(PEOPLE, "character-robot")]),
    "alien": (0.3, ["alien", "martian"], [(PEOPLE, "character-alien")]),
    "zombie": (0.3, ["zombie"], [(PEOPLE, "character-zombie")]),
    "astronaut": (0.3, ["astronaut", "spaceman"], [("space-kit", "astronautA")]),
    # ---- things (Kenney) ----
    "sofa": (0.45, ["sofa", "couch"], [("furniture-kit", "loungeSofa")]),
    "bed": (0.45, ["bed"], [("furniture-kit", "bedDouble")]),
    "tv": (0.35, ["tv", "television", "telly"], [("furniture-kit", "televisionModern")]),
    "laptop": (0.2, ["laptop"], [("furniture-kit", "laptop")]),
    "table_lamp": (0.25, ["table lamp", "desk lamp"], [("furniture-kit", "lampRoundTable")]),
    "fridge": (0.45, ["fridge", "refrigerator"], [("furniture-kit", "kitchenFridge")]),
    "train": (0.4, ["train", "toy train", "steam train", "locomotive"], [("holiday-kit", "train-locomotive")]),
    "christmas_tree": (0.45, ["decorated tree", "christmas tree with lights"], [("holiday-kit", "tree-decorated")]),
    "gingerbread": (0.22, ["gingerbread man", "gingerbread"], [("holiday-kit", "gingerbread-man")]),
    "nutcracker": (0.3, ["nutcracker", "toy soldier"], [("holiday-kit", "nutcracker")]),
    "sled": (0.35, ["sled", "sleigh"], [("holiday-kit", "sled-long")]),
    "spaceship": (0.45, ["spaceship", "space ship", "rocket ship", "starship", "ufo", "flying saucer"], [("space-kit", "craft_speederA"), ("space-kit", "craft_racer"), ("space-kit", "craft_cargoA")]),
    "carrot": (0.18, ["carrot"], [("nature-kit", "crop_carrot")]),
    "melon": (0.2, ["melon", "watermelon"], [("nature-kit", "crop_melon")]),
    "sword": (0.4, ["sword"], [(PEOPLE, "weapon-sword")]),
    "shield": (0.3, ["shield"], [(PEOPLE, "shield-round")]),
    "suitcase": (0.3, ["suitcase", "luggage"], [(PEOPLE, "item-luggage-upright")]),
}
