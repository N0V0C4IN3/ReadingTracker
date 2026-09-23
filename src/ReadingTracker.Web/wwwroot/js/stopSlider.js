// The stop slider (Components/StopSlider.razor) covers the whole book, but the reader cannot
// stop behind where they already were. That is held here, on the element, before Blazor reads
// the value: pulled back past the floor, the thumb stays at it. Done in C# instead, the thumb
// would sit wherever it was dropped, since Blazor sees the same value it drew last time and
// has nothing to change.
export function holdFloor(slider) {
    slider.addEventListener("input", () => {
        const floor = Number(slider.dataset.floor);
        if (slider.valueAsNumber < floor) {
            slider.value = floor;
        }
    });
}
