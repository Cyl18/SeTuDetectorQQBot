import os
from typing import Any, Iterable, List, Tuple, Union

import six
import tensorflow as tf
import sys
import deepdanbooru as dd
from time import sleep

def evaluate_image(
    image_input: Union[str, six.BytesIO], model: Any, tags: List[str], threshold: float
) -> Iterable[Tuple[str, float]]:
    width = model.input_shape[2]
    height = model.input_shape[1]

    image = dd.data.load_image_for_evaluate(
        image_input, width=width, height=height)

    image_shape = image.shape
    image = image.reshape(
        (1, image_shape[0], image_shape[1], image_shape[2]))
    y = model.predict(image)[0]

    result_dict = {}

    for i, tag in enumerate(tags):
        result_dict[tag] = y[i]

    for tag in tags:
        if result_dict[tag] >= threshold:
            yield tag, result_dict[tag]


def evaluate(target_paths, project_path, model_path, tags_path, threshold, allow_gpu, compile_model, allow_folder, folder_filters, verbose):
    if not allow_gpu:
        os.environ['CUDA_VISIBLE_DEVICES'] = '-1'

    if not model_path and not project_path:
        raise Exception('You must provide project path or model path.')

    if not tags_path and not project_path:
        raise Exception('You must provide project path or tags path.')

    

    if model_path:
        if verbose:
            print(f'Loading model from {model_path} ...')
        model = tf.keras.models.load_model(model_path, compile=compile_model)
    else:
        if verbose:
            print(f'Loading model from project {project_path} ...')
        model = dd.project.load_model_from_project(project_path, compile_model=compile_model)

    if tags_path:
        if verbose:
            print(f'Loading tags from {tags_path} ...')
        tags = dd.data.load_tags(tags_path)
    else:
        if verbose:
            print(f'Loading tags from project {project_path} ...')
        tags = dd.project.load_tags_from_project(project_path)


    target_image_paths = []

    target_path = 'D:\\WARNING-DELETE-IF-FILE-IN'
    
    while True:
        try:
            target_image_paths = dd.io.get_image_file_paths_recursive(target_path, folder_filters)
    
            target_image_paths = dd.extra.natural_sorted(target_image_paths)
            for image_path in target_image_paths:
                sleep(0.1)
                print(f'MAGIC{image_path}', end='')
                for tag, score in evaluate_image(image_path, model, tags, threshold):
                    print(f'艹{score:05.3f}丂{tag}', end = '')
                print()
                os.remove(image_path)
            sleep(0.05)
            sys.stdout.flush()
        except:
            pass
    


